using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     ASP.NET Core authentication handler serving both Schemata access-token schemes.
///     It dispatches on the wire scheme of the Authorization header per RFC 9449 §7.1:
///     "DPoP" requests validate a DPoP proof bound to the token, while "Bearer" requests
///     reject DPoP-bound tokens per §7.2. Direct compatibility sign-in calls delegate
///     issuance to <see cref="IAuthorizationSignInService" /> and HTTP writing to
///     <see cref="IAuthorizationSignInHttpWriter" />.
/// </summary>
/// <remarks>
///     The DPoP scheme and its services are registered only by
///     <see cref="Features.DPopFlowFeature{TApp}" />; the nullable proof validator and
///     nonce store keep the handler constructible for hosts without that feature, whose
///     DPoP flavor instance then never exists.
/// </remarks>
public class SchemataAuthenticationHandler<TApp>(
    IOptionsMonitor<SchemataAuthenticationHandlerOptions> options,
    ILoggerFactory                                        logger,
    UrlEncoder                                            encoder,
    TokenService                                          issuer,
    ITokenStore<SchemataToken>                                   tokens,
    IAuthorizationSignInService                           signIns,
    IAuthorizationSignInHttpWriter                        writer,
    IOptions<DPopOptions>                                 dpop,
    DPopProofValidator?                                   proofs = null,
    [FromKeyedServices(SecurityConstants.TokenTypes.Nonce)] ITokenStore<SchemataToken>? nonces = null
) : SignInAuthenticationHandler<SchemataAuthenticationHandlerOptions>(options, logger, encoder)
    where TApp : SchemataApplication
{
    /// <summary>
    ///     Returns <c>true</c> when the grant type indicates a user-present flow
    ///     that can receive an ID token.
    /// </summary>
    public static bool IsUserGrant(IDictionary<string, string?> items) {
        items.TryGetValue(Properties.GrantType, out var grant);
        return grant is GrantTypes.AuthorizationCode or GrantTypes.RefreshToken or GrantTypes.TokenExchange;
    }

    /// <summary>
    ///     Determines whether a refresh token should be issued.
    ///     Returns <c>true</c> for the <c>refresh_token</c> grant (rotation),
    ///     <c>false</c> for <c>client_credentials</c> and the <c>jwt-bearer</c> assertion grant
    ///     (RFC 7521 §4.1: assertion grants yield short-lived access tokens; clients re-assert
    ///     instead of refreshing), and otherwise follows the presence of the
    ///     <c>offline_access</c> scope.
    /// </summary>
    public static bool ShouldIssueRefreshToken(IDictionary<string, string?> items) {
        if (!items.TryGetValue(Properties.GrantType, out var grant) || string.IsNullOrWhiteSpace(grant)) {
            return false;
        }

        switch (grant) {
            case GrantTypes.RefreshToken:
                return true;
            case GrantTypes.ClientCredentials:
            case GrantTypes.JwtBearer:
                return false;
            default:
                items.TryGetValue(Properties.Scope, out var scope);
                return ScopeParser.Contains(scope, Scopes.OfflineAccess);
        }
    }

    /// <summary>
    ///     Creates a signed OIDC ID token (JWT) with optional <c>at_hash</c>,
    ///     <c>c_hash</c>, and <c>nonce</c> claims. The <c>auth_time</c> claim, REQUIRED when
    ///     <c>max_age</c> was used (OpenID Connect Core 1.0 §2), is minted by
    ///     <see cref="AdviceClaimsAuthenticationContext" /> whenever the authentication context
    ///     asserts it.
    /// </summary>
    /// <param name="token">The <see cref="TokenService" /> used for signing.</param>
    /// <param name="items">Authentication properties dictionary.</param>
    /// <param name="claims">Claims to include in the ID token.</param>
    /// <param name="lifetime">ID token validity duration.</param>
    /// <param name="at">Access token value for <c>at_hash</c> computation.</param>
    /// <param name="code">Authorization code value for <c>c_hash</c> computation.</param>
    public static Task<string> CreateIdToken(
        TokenService                 token,
        IDictionary<string, string?> items,
        List<Claim>                  claims,
        TimeSpan                     lifetime,
        string?                      at,
        string?                      code
    ) {
        items.TryGetValue(Properties.Nonce, out var nonce);

        return token.CreateIdToken(claims, lifetime, at, code, nonce);
    }

    /// <summary>
    ///     Creates and persists a token entity (access, refresh, or ID).
    ///     For JWT/JWE formats, the reference IS the encoded token value;
    ///     for opaque reference tokens, a separate random reference is generated
    ///     and the JWT is stored as the payload for later introspection.
    ///     Returns the value that should be emitted to the client.
    /// </summary>
    /// <param name="tokens">Token storage manager.</param>
    /// <param name="token">Token service for JWT/JWE creation.</param>
    /// <param name="claims">Claims to embed.</param>
    /// <param name="format">Token serialization format (JWT, JWE, or Reference).</param>
    /// <param name="lifetime">Token validity duration.</param>
    /// <param name="type">Token type (e.g., <see cref="TokenTypes.AccessToken" />).</param>
    /// <param name="subject">Resource owner subject.</param>
    /// <param name="application">Issuing client application name.</param>
    /// <param name="authorization">Linked authorization/consent record name.</param>
    /// <param name="session">OP session identifier.</param>
    /// <param name="time">Clock for the token's create and expiry timestamps.</param>
    /// <param name="ct">A cancellation token.</param>
    public static async Task<string> CreateTokenAsync(
        ITokenStore<SchemataToken>   tokens,
        TokenService          token,
        List<Claim>           claims,
        string?               format,
        TimeSpan              lifetime,
        string                type,
        string?               subject,
        string?               application,
        string?               authorization,
        string?               session,
        TimeProvider          time,
        CancellationToken     ct
    ) {
        var jti         = Guid.NewGuid().ToString("n");
        var tokenClaims = new List<Claim>(claims) { new(Claims.JwtId, jti) };
        var typ         = type == TokenTypes.AccessToken ? TokenMediaTypes.AccessToken : null;

        string value;
        string reference;

        switch (format) {
            case TokenFormats.Jwt:
                value     = await token.CreateToken(tokenClaims, lifetime, typ: typ);
                reference = value;
                break;

            case TokenFormats.Jwe:
                value     = await token.CreateToken(tokenClaims, lifetime, true, typ);
                reference = value;
                break;

            case TokenFormats.Reference:
            default:
                reference = token.CreateReference();
                value     = reference;
                break;
        }

        var payload = format == TokenFormats.Reference ? await token.CreateToken(tokenClaims, lifetime) : value;

        var now = time.GetUtcNow().UtcDateTime;
        var entity = new SchemataToken {
            Name              = jti,
            Type              = type,
            Format            = format,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = payload,
            Parent            = subject,
            ExpireTime        = now + lifetime,
            Application       = application,
            Authorization     = authorization,
            SessionId         = session,
        };
        await tokens.CreateAsync(entity, ct);

        return value;
    }

    private const string ChallengeErrorItem       = "Schemata.Authorization.DpopChallengeError";
    private const string ChallengeNonceItem       = "Schemata.Authorization.DpopChallengeNonce";
    private const string BearerChallengeErrorItem = "Schemata.Authorization.BearerChallengeError";
    private const string NonceProvider            = "dpop-rs";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        var ct = Context.RequestAborted;

        // RFC 9449 §7.1: with both schemes supported the token is interpreted per the scheme
        // presented on the wire; each registration serves its own scheme's requests.
        var flavor = Scheme.Name.Equals(Schemes.Dpop, StringComparison.Ordinal) ? Schemes.Dpop : Schemes.Bearer;

        var header = Request.Headers.Authorization.ToString();
        var space  = header.IndexOf(' ');
        if (space <= 0 || !header[..space].Equals(flavor, StringComparison.OrdinalIgnoreCase)) {
            return AuthenticateResult.NoResult();
        }

        var token = header[(space + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(token)) {
            // §7.1 Figure 15: a challenge for a request without credentials carries no error.
            return flavor == Schemes.Dpop ? StageDpopChallenge(string.Empty) : AuthenticateResult.NoResult();
        }

        var entity = await tokens.FindByReferenceIdAsync(token, ct);
        if (string.IsNullOrWhiteSpace(entity?.Application)
         || entity.Type != TokenTypes.AccessToken
         || entity.Status != TokenStatuses.Valid) {
            if (flavor == Schemes.Dpop) {
                StageDpopChallenge(OAuthErrors.InvalidToken);
            }

            return AuthenticateResult.NoResult();
        }

        var principal = entity.Format switch {
            TokenFormats.Reference when !string.IsNullOrWhiteSpace(entity.Payload) => await ValidateReferencePayload(entity.Payload, entity.Application),
            TokenFormats.Jwt or TokenFormats.Jwe => await issuer.Validate(token, entity.Application),
            var _                                => null,
        };

        if (principal is null) {
            return AuthenticateResult.NoResult();
        }

        if (principal.Identity is not ClaimsIdentity id) {
            return AuthenticateResult.NoResult();
        }

        var claims = id.Claims.Where(c => c.Type != IdentityClaims.Subject)
                       .Append(new(IdentityClaims.Subject, entity.Parent ?? string.Empty))
                       .ToList();
        principal = new(new ClaimsIdentity(claims, Scheme.Name, IdentityClaims.Subject, IdentityClaims.Role));

        var jkt = DPopProofValidator.ReadBoundThumbprint(principal);

        // §7.2 Figure 18: a DPoP-bound token received via Bearer MUST be rejected, with the
        // error on the Bearer challenge — the scheme the client actually used.
        if (flavor == Schemes.Bearer) {
            if (jkt is not null) {
                StageBearerChallenge(OAuthErrors.InvalidToken);
                return AuthenticateResult.NoResult();
            }

            return AuthenticateResult.Success(new(principal, Scheme.Name));
        }

        // Unreachable without the DPoP scheme; the guard keeps the DPoP branch honest when the
        // feature's services were not registered.
        if (proofs is null || nonces is null) {
            return AuthenticateResult.NoResult();
        }

        var proof = Request.Headers[Headers.Dpop]
                       .Where(v => !string.IsNullOrWhiteSpace(v))
                       .FirstOrDefault();
        if (proof is null) {
            StageDpopChallenge(OAuthErrors.InvalidDpopProof);
            return AuthenticateResult.NoResult();
        }

        var htu      = new Uri($"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}");
        var nonceKey = NonceProvider;

        string thumbprint;
        try {
            thumbprint = await proofs.ValidateAsync(proof, Request.Method, htu, token, nonceKey, entity.Application, ct);
        } catch (OAuthException ex) when (ex.Status == OAuthErrors.UseDpopNonce) {
            // §9: 401 use_dpop_nonce with the current value in a DPoP-Nonce response header;
            // the client retries with it in the proof's nonce claim.
            StageDpopChallenge(
                OAuthErrors.UseDpopNonce,
                (await nonces.GetOrCreateAsync(
                    null, nonceKey, entity.Application, null, dpop.Value.NonceLifetime, ct)).Value);
            return AuthenticateResult.NoResult();
        } catch (OAuthException) {
            StageDpopChallenge(OAuthErrors.InvalidDpopProof);
            return AuthenticateResult.NoResult();
        }

        if (jkt is not null && !string.Equals(jkt, thumbprint, StringComparison.Ordinal)) {
            StageDpopChallenge(OAuthErrors.InvalidToken);
            return AuthenticateResult.NoResult();
        }

        // Tokens issued without cnf stay presentable via the DPoP scheme during the BCP
        // transition; the proof is still enforced, only cnf-bound tokens are compared.
        return AuthenticateResult.Success(new(principal, Scheme.Name));
    }

    /// <summary>Validates a Reference-format stored payload; an invalid payload
    ///     yields no principal.</summary>
    /// <param name="payload">Stored payload of the token row.</param>
    /// <param name="application">Application canonical name used as the validation audience.</param>
    private async Task<ClaimsPrincipal?> ValidateReferencePayload(string payload, string? application) {
        return await issuer.Validate(payload, application);
    }

    private AuthenticateResult StageDpopChallenge(string error, string? nonce = null) {
        Context.Items[ChallengeErrorItem] = error;

        if (nonce is not null) {
            Context.Items[ChallengeNonceItem] = nonce;
        }

        return AuthenticateResult.NoResult();
    }

    private AuthenticateResult StageBearerChallenge(string error) {
        Context.Items[BearerChallengeErrorItem] = error;

        return AuthenticateResult.NoResult();
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        // §7.2 Figures 17/18: each scheme of this handler advertises its own challenge; the
        // error rides the challenge corresponding to the mechanism that failed. Instances
        // share the request item store, so each flavor consumes only its own staged state.
        var isDpop = Scheme.Name.Equals(Schemes.Dpop, StringComparison.Ordinal);

        if (!isDpop && Context.Items[BearerChallengeErrorItem] is string bearerError) {
            WriteChallenge(BuildBearerChallenge(bearerError));
        } else if (isDpop && Context.Items[ChallengeErrorItem] is string error) {
            WriteChallenge(BuildChallenge(error, dpop.Value.SigningAlgorithms));

            if (Context.Items[ChallengeNonceItem] is string nonce) {
                Response.Headers[Headers.DpopNonce] = nonce;
            }
        } else {
            WriteChallenge(isDpop
                ? BuildChallenge(string.Empty, dpop.Value.SigningAlgorithms)
                : Schemes.Bearer);
        }

        return Task.CompletedTask;
    }

    private void WriteChallenge(string value) {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = StringValues.Concat(Response.Headers.WWWAuthenticate, value);
    }

    /// <summary>
    ///     Shapes a DPoP WWW-Authenticate value per RFC 9449 §7.1: the scheme name, an
    ///     error parameter when a presented token failed authentication, and the
    ///     space-delimited <c>algs</c> list of acceptable proof algorithms.
    /// </summary>
    internal static string BuildChallenge(string? error, IEnumerable<string> algorithms) {
        var builder = new StringBuilder(Schemes.Dpop);
        var first   = true;

        if (!string.IsNullOrWhiteSpace(error)) {
            builder.Append(" error=\"").Append(error).Append('"');
            first = false;
        }

        var algs = string.Join(' ', algorithms.OrderBy(a => a, StringComparer.Ordinal));
        if (algs.Length > 0) {
            builder.Append(first ? " algs=\"" : ", algs=\"").Append(algs).Append('"');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Shapes the Bearer companion challenge per RFC 9449 §7.2 Figure 18: the error
    ///     travels on the scheme the client used, without DPoP-specific parameters.
    /// </summary>
    internal static string BuildBearerChallenge(string error) {
        return $"{Schemes.Bearer} error=\"{error}\", error_description=\"{SchemataResources.GetResourceString(SchemataResources.INVALID_TOKEN)}\"";
    }


    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) { return Task.CompletedTask; }

    protected override async Task HandleSignInAsync(
        ClaimsPrincipal          principal,
        AuthenticationProperties? properties
    ) {
        var response = await signIns.IssueAsync(
            principal, properties?.Items, AuthorizationSignInResponseKind.Token, Context.RequestAborted);
        await writer.WriteAsync(Context, response, Context.RequestAborted);
    }
}
