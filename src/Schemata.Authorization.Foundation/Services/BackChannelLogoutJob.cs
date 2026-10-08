using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     OIDC Back-Channel Logout HTTP POST per
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>.
///     Signs the logout token at execution time from the recipient facts persisted as job
///     variables, then POSTs it form-urlencoded to the relying party. Requires the
///     <c>uri</c>, <c>audience</c>, and either <c>subject</c> or <c>sid</c> variables —
///     §2.4 mandates that the logout token carry <c>sub</c> or <c>sid</c>. Missing facts,
///     signing failure, a non-success response, or a non-cancellation transport failure becomes
///     a failed execution. Cancellation, including HTTP timeout, leaves it for re-dispatch.
/// </summary>
[ScheduledJob(JobKey)]
public sealed class BackChannelLogoutJob(
    IHttpClientFactory                     factory,
    TokenService                           issuer,
    IOptions<SchemataAuthorizationOptions> options,
    TimeProvider?                          time = null
) : IScheduledJob
{
    /// <summary>Stable scheduler key persisted on back-channel logout execution rows.</summary>
    public const string JobKey = "schemata.authorization.logout.backchannel";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var uri      = Require(context, VariableKeys.Uri);
        var audience = Require(context, VariableKeys.Audience);
        context.Variables.TryGetValue(VariableKeys.Subject, out var subject);
        context.Variables.TryGetValue(VariableKeys.SessionId, out var session);
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(session)) {
            // OIDC Back-Channel Logout §2.4 — the logout token MUST carry either sub or sid.
            throw new InvalidOperationException(
                "Back-channel logout job requires the 'subject' or 'sid' variable.");
        }
        context.Variables.TryGetValue(VariableKeys.SigningAlgorithm, out var algorithm);

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString("n")),
            new(Claims.Events, "{\"" + EventTypes.LogoutEvent + "\":{}}", JsonClaimValueTypes.Json),
            new(Claims.Audience, audience),
        };
        if (!string.IsNullOrWhiteSpace(options.Value.Issuer)) claims.Add(new(Claims.Issuer, options.Value.Issuer));
        if (!string.IsNullOrWhiteSpace(subject)) claims.Add(new(IdentityClaims.Subject, subject));
        if (!string.IsNullOrWhiteSpace(session)) claims.Add(new(Claims.SessionId, session));

        var issuedAt = _time.GetUtcNow();
        await using var signing = await issuer.BeginSigningAsync(algorithm, ct);
        var jwt = issuer.CreateToken(
            signing, signing.Signing, claims, issuedAt, issuedAt + BackChannelLogoutService.LogoutTokenLifetime,
            typ: TokenMediaTypes.Logout);

        var client = factory.CreateClient(nameof(BackChannelLogoutService<>));
        client.Timeout = BackChannelLogoutService.Timeout;
        var content  = new FormUrlEncodedContent([new(Parameters.LogoutToken, jwt)]);
        var response = await client.PostAsync(uri, content, ct);
        response.EnsureSuccessStatusCode();

        if (context.Execution is { } execution) {
            execution.Output = uri;
        }
    }

    #endregion

    private static string Require(JobContext context, string key) {
        if (!context.Variables.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException($"Back-channel logout job requires the '{key}' variable.");
        }

        return value;
    }

    /// <summary>
    ///     Defines scheduler variable names consumed by back-channel logout jobs.
    /// </summary>
    internal static class VariableKeys
    {
        /// <summary>Scheduler variable containing the relying party logout URI.</summary>
        public const string Uri = "uri";

        /// <summary>Scheduler variable containing the logout token audience (the relying party's client_id).</summary>
        public const string Audience = "audience";

        /// <summary>Scheduler variable containing the recipient-specific subject identifier.</summary>
        public const string Subject = "subject";

        /// <summary>Scheduler variable containing the OP session identifier.</summary>
        public const string SessionId = "sid";

        /// <summary>Scheduler variable containing the negotiated logout-token signing algorithm.</summary>
        public const string SigningAlgorithm = "signing_algorithm";
    }
}
