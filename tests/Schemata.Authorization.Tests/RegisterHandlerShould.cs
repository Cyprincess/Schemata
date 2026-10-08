using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Schemata.Abstractions.Advisors;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RegisterHandlerShould : IDisposable
{
    private readonly ServiceProvider _provider = new ServiceCollection().BuildServiceProvider();
    private readonly IDisposable _ambient;
    private readonly List<SchemataSecurity> _rows = new();

    public RegisterHandlerShould() {
        _ambient = AdviceContext.Establish(new(_provider));
    }

    public void Dispose() {
        _ambient.Dispose();
        _provider.Dispose();
    }
    private readonly List<SchemataApplication> _registered = new();

    private (RegisterHandler<SchemataApplication> Handler, Mock<IApplicationManager<SchemataApplication>> Apps, Mock<ITokenStore<SchemataToken>> Tokens, List<SchemataToken> Store, SchemataAuthorizationOptions Options, Mock<ISecurityStore<SchemataSecurity>> Securities) Create(
        Action<SchemataAuthorizationOptions>? configure = null,
        HttpMessageHandler?                   handler   = null,
        ISoftwareStatementValidator?          softwareStatements = null,
        TimeProvider?                         time               = null
    ) {
        var options = new SchemataAuthorizationOptions {
            Issuer = "https://as.example",
        };
        configure?.Invoke(options);

        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();

        apps.Setup(m => m.CreateAsync(It.IsAny<SchemataApplication>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemataApplication a, CancellationToken _) => {
                a.Name = "registered-resource";
                a.CanonicalName = "applications/registered-resource";
                _registered.Add(a);
                return a;
            });
        apps.Setup(m => m.FindByClientIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? id, CancellationToken _) => _registered.FirstOrDefault(a => a.ClientId == id));
        apps.Setup(m => m.UpdateAsync(It.IsAny<SchemataApplication>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        apps.Setup(m => m.DeleteAsync(It.IsAny<SchemataApplication>(), It.IsAny<CancellationToken>()))
            .Returns((SchemataApplication a, CancellationToken _) => {
                _registered.Remove(a);
                return Task.CompletedTask;
            });
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();
        securities
            .Setup(s => s.CreateAsync(It.IsAny<SchemataSecurity>(), It.IsAny<CancellationToken>(),
                                      It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .Callback<SchemataSecurity, CancellationToken, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>((row, _, _) => _rows.Add(row))
            .ReturnsAsync((SchemataSecurity row, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => row);
        securities
            .Setup(s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string?, string?, string?, string?, CancellationToken>((parent, kind, _, status, _) =>
                Enumerate(_rows.Where(row => row.Parent == parent
                                          && (kind is null || row.Kind == kind)
                                          && (status is null || row.Status == status))));
        securities
            .Setup(s => s.UpdateAsync(It.IsAny<SchemataSecurity>(), It.IsAny<CancellationToken>(),
                                      It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .Returns(Task.CompletedTask);

        var verifier = new Mock<ISecretVerifier>();
        verifier
            .Setup(v => v.HashAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string presented, string? _, CancellationToken _) => $"hashed:{presented}");

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        var stored = new List<SchemataToken>();
        tokens.Setup(m => m.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .Callback<SchemataToken, CancellationToken, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>((t, _, _) => stored.Add(t))
            .ReturnsAsync((SchemataToken t, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => t);
        tokens.Setup(m => m.FindByReferenceIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? r, CancellationToken _) => stored.FirstOrDefault(t => t.ReferenceId == r));

        tokens.Setup(m => m.TryRotateAsync(
                   It.IsAny<SchemataToken>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(), It.IsAny<CancellationToken>(),
                   It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .ReturnsAsync((SchemataToken predecessor, IReadOnlyCollection<SchemataToken> successors, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => {
                // The winning rotation: the predecessor is redeemed and the successors are
                // published in one storage commit.
                predecessor.Status = TokenStatuses.Redeemed;
                stored.AddRange(successors);
                return true;
            });
        tokens.Setup(m => m.RevokeByApplicationAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? application, CancellationToken _) => {
                var matches = stored.Where(t => t.Application == application
                                             && t.Status != TokenStatuses.Revoked
                                             && t.Type != SecurityConstants.TokenTypes.SessionParticipant)
                                    .ToList();
                foreach (var match in matches) {
                    match.Status = TokenStatuses.Revoked;
                }

                return matches.Count;
            });
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler ?? new NotFoundHandler()) { Timeout = TimeSpan.FromSeconds(10) });

        var initialAccess = new Mock<IInitialAccessTokenValidator>();
        initialAccess.Setup(v => v.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(true);

        var registerHandler = new RegisterHandler<SchemataApplication>(
            apps.Object, tokens.Object, TestSecurityKeys.CreateTokenService(options),
            Options.Create(options), http.Object, securities.Object, verifier.Object,
            softwareStatements: softwareStatements,
            initialAccess: initialAccess.Object,
            time: time);

        return (registerHandler, apps, tokens, stored, options, securities);
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(IEnumerable<SchemataSecurity> rows) {
        foreach (var row in rows) {
            yield return row;
        }
    }

    [Theory]
    [InlineData(SigningAlgorithms.HmacSha256)]
    [InlineData(SigningAlgorithms.HmacSha512)]
    public async Task Registered_Jwt_Secret_Authenticates_The_Client(string algorithm) {
        var (handler, apps, tokens, _, options, securities) = Create(o => o.AllowedClientAuthMethods.Add(ClientAuthMethods.ClientSecretJwt));
        var registered = await handler.HandleAsync(new RegisterRequest {
            TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretJwt,
            TokenEndpointAuthSigningAlg = algorithm,
            GrantTypes = [GrantTypes.ClientCredentials],
        }, null, CancellationToken.None);
        Assert.NotNull(registered.ClientSecret);
        var now = DateTime.UtcNow;
        var assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Issuer = registered.ClientId,
            Audience = options.Issuer,
            Claims = new Dictionary<string, object> { ["sub"] = registered.ClientId!, ["jti"] = Guid.NewGuid().ToString("N") },
            IssuedAt = now, Expires = now.AddMinutes(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(registered.ClientSecret)), algorithm),
        });
        tokens.Setup(t => t.GetOrCreateAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                                           It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((string? parent, string provider, string key, string? value, TimeSpan _, CancellationToken _) =>
                  new SchemataToken { Parent = parent, Provider = provider, Key = key, Value = value });
        var http = new Mock<IHttpClientFactory>();
        http.Setup(h => h.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var authentication = new ClientSecretJwtAuthentication<SchemataApplication>(apps.Object, Options.Create(options),
            http.Object, Mock.Of<ICacheProvider>(), Options.Create(new SchemataSecurityOptions()), securities.Object,
            new ClientAssertionValidator(tokens.Object, Options.Create(new SchemataAuthorizationOptions())), new ClientAssertionChannel());

        var client = await authentication.AuthenticateAsync(null, new() {
            [Parameters.ClientAssertionType] = [ClientAssertionTypes.JwtBearer],
            [Parameters.ClientAssertion] = [assertion],
        }, null, CancellationToken.None);

        Assert.Equal(registered.ClientId, client?.ClientId);
    }

    [Theory]
    [InlineData(ClientAuthMethods.ClientSecretJwt, ClientAuthMethods.ClientSecretBasic)]
    [InlineData(ClientAuthMethods.ClientSecretJwt, ClientAuthMethods.ClientSecretPost)]
    [InlineData(ClientAuthMethods.ClientSecretBasic, ClientAuthMethods.ClientSecretJwt)]
    [InlineData(ClientAuthMethods.ClientSecretPost, ClientAuthMethods.ClientSecretJwt)]
    public async Task Reject_Secret_Representation_Change_Before_Replacing_Client(string original, string replacement) {
        var (handler, apps, _, _, _, _) = Create(o => o.AllowedClientAuthMethods.Add(ClientAuthMethods.ClientSecretJwt));
        var registered = await handler.HandleAsync(new RegisterRequest {
            TokenEndpointAuthMethod = original, GrantTypes = [GrantTypes.ClientCredentials],
        }, null, CancellationToken.None);
        var client = await apps.Object.FindByClientIdAsync(registered.ClientId!, CancellationToken.None);

        var error = await Assert.ThrowsAsync<OAuthException>(() => handler.ReplaceAsync(registered.ClientId, new RegisterRequest {
            ClientId = registered.ClientId, TokenEndpointAuthMethod = replacement, GrantTypes = [GrantTypes.ClientCredentials],
        }, registered.RegistrationAccessToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
        Assert.Equal(original, client!.TokenEndpointAuthMethod);
        apps.Verify(a => a.UpdateAsync(It.IsAny<SchemataApplication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null, SecurityConstants.Algorithms.Rsa, SecurityConstants.Statuses.Valid, true)]
    [InlineData("ES384", SecurityConstants.Algorithms.P384, SecurityConstants.Statuses.Valid, true)]
    [InlineData("ES384", SecurityConstants.Algorithms.P384, SecurityConstants.Statuses.Retired, false)]
    [InlineData("ES512", SecurityConstants.Algorithms.Rsa, SecurityConstants.Statuses.Valid, false)]
    [InlineData("none", SecurityConstants.Algorithms.Rsa, SecurityConstants.Statuses.Valid, false)]
    [InlineData(null, SecurityConstants.Algorithms.P384, SecurityConstants.Statuses.Valid, false)]
    public async Task Register_Only_Serviceable_Effective_IdToken_Signing(string? requested, string available, string status, bool accepted) {
        var (handler, _, _, _, options, securities) = Create();
        TestSecurityKeys.AddSigningRow(securities.Object, options.Issuer!, available).Status = status;
        var request = new RegisterRequest {
            Scope = Scopes.OpenId, RedirectUris = ["https://rp.example/cb"], IdTokenSignedResponseAlg = requested,
        };
        if (accepted) {
            var response = await handler.HandleAsync(request, null, CancellationToken.None);
            Assert.Equal(requested ?? SigningAlgorithms.RsaSha256, response.IdTokenSignedResponseAlg);
        } else {
            var error = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(request, null, CancellationToken.None));
            Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
        }
    }

    [Fact]
    public async Task Register_A_Confidential_Web_Client_With_201_Fields() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            ClientName   = "RP",
            FrontChannelLogoutSessionRequired = true,
        }, null, CancellationToken.None);

        Assert.NotNull(response.ClientId);
        Assert.NotEmpty(response.ClientId);
        Assert.NotNull(response.ClientSecret);
        Assert.Equal(0, response.ClientSecretExpiresAt);
        Assert.NotNull(response.ClientIdIssuedAt);
        Assert.Equal("RP", response.ClientName);
        Assert.True(response.FrontChannelLogoutSessionRequired);
        Assert.NotNull(response.GrantTypes);
        Assert.Contains(GrantTypes.AuthorizationCode, response.GrantTypes);
    }
    [Fact]
    public async Task Source_The_Client_Secret_From_A_Password_Security_Row() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        var row = Assert.Single(_rows);
        Assert.Equal("applications/registered-resource", row.Parent);
        Assert.Equal(response.ClientId, row.Key);
        Assert.Null(row.Name);
        Assert.Equal(SecurityConstants.Kinds.Password, row.Kind);
        Assert.Equal(SecurityConstants.Usages.Authentication, row.Usage);
        Assert.Equal(SecurityConstants.Algorithms.Pbkdf2, row.Algorithm);
        Assert.Equal(SecurityConstants.Statuses.Valid, row.Status);
        Assert.Equal($"hashed:{response.ClientSecret}", row.Value);
    }
    [Fact]
    public async Task Source_The_Client_Jwks_From_A_Jwks_Security_Row() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            Jwks         = JsonDocument.Parse("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""").RootElement.Clone(),
        }, null, CancellationToken.None);

        var row = Assert.Single(_rows, value => value.Kind == SecurityConstants.Kinds.Jwks);
        Assert.Equal("applications/registered-resource", row.Parent);
        Assert.Equal(response.ClientId, row.Key);
        Assert.Null(row.Name);
        Assert.Equal(SecurityConstants.Usages.Authentication, row.Usage);
        Assert.Equal(SecurityConstants.Statuses.Valid, row.Status);
        Assert.Equal("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""", row.Value);
    }
    [Fact]
    public async Task Source_The_Client_Jwks_Uri_From_A_Jwks_Uri_Security_Row() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            JwksUri      = "https://rp.example/jwks.json",
        }, null, CancellationToken.None);

        var row = Assert.Single(_rows, value => value.Kind == SecurityConstants.Kinds.JwksUri);
        Assert.Equal("applications/registered-resource", row.Parent);
        Assert.Equal(response.ClientId, row.Key);
        Assert.Null(row.Name);
        Assert.Equal(SecurityConstants.Usages.Authentication, row.Usage);
        Assert.Equal(SecurityConstants.Statuses.Valid, row.Status);
        Assert.Equal("https://rp.example/jwks.json", row.Value);
    }

    [Fact]
    public async Task Reject_Missing_Redirect_Uris_With_Invalid_Redirect_Uri() {
        var (handler, _, _, _, _, _) = Create();

        // The openid scope selects the interactive profile, which keeps the redirect obligation;
        // only OAuth-only registrations may omit redirect_uris.
        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
            new() { Scope = "openid" }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }

    [Theory]
    [InlineData("http://rp.example/cb",         ApplicationTypes.Web)]     // http on web
    [InlineData("https://127.0.0.1/cb",         ApplicationTypes.Web)]     // loopback on web
    [InlineData("http://localhost/cb",          ApplicationTypes.Native)]  // localhost excluded per RFC 8252 §8.3
    [InlineData("https://user@rp.example/cb", ApplicationTypes.Web)]     // userinfo rejected on every profile
    public async Task Reject_Invalid_Redirect_Uris_Per_Application_Type(string uri, string applicationType) {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris     = [uri],
                ApplicationType  = applicationType,
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }

    [Fact]
    public async Task Accept_A_Native_Custom_Scheme_And_Loopback_Ip() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris    = ["com.example.app:/cb", "http://127.0.0.1:49152/cb"],
            ApplicationType = ApplicationTypes.Native,
        }, null, CancellationToken.None);

        Assert.NotEmpty(response.ClientId!);
    }

    [Fact]
    public async Task Reject_Response_Type_Without_Backing_Grant_Type() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris  = ["https://rp.example/cb"],
                GrantTypes    = [GrantTypes.ClientCredentials],
                ResponseTypes = [ResponseTypes.Code],
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Reject_Auth_Method_Outside_The_Server_Allowed_Set() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris            = ["https://rp.example/cb"],
                TokenEndpointAuthMethod = "tls_client_auth",
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Reject_Jwks_And_Jwks_Uri_Together() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris = ["https://rp.example/cb"],
                Jwks         = JsonDocument.Parse("""{"keys":[]}""").RootElement.Clone(),
                JwksUri      = "https://rp.example/jwks.json",
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Reject_Sector_Identifier_Not_Covering_Redirect_Hosts() {
        var handler404 = new StubHandler("""["https://other.example/cb"]""");
        var (handler, _, _, _, _, _) = Create(handler: handler404);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris          = ["https://rp.example/cb"],
                SectorIdentifierUri   = "https://sector.example/si",
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Reject_Uri_Family_Host_Inconsistent_With_Redirect_Uris() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris = ["https://rp.example/cb"],
                LogoUri      = "https://elsewhere.example/logo.png",
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body),
            });
        }
    }
    [Fact]
    public async Task Issue_A_Paired_Registration_Token_And_Client_Uri() {
        var (handler, _, tokens, store, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        Assert.NotNull(response.RegistrationAccessToken);
        Assert.NotEmpty(response.RegistrationAccessToken);
        Assert.Equal($"https://as.example/Connect/Register/{response.ClientId}", response.RegistrationClientUri);

        var token = store.Single();
        Assert.Equal(TokenTypes.Registration, token.Type);
        Assert.Equal(TokenFormats.Reference,   token.Format);
        Assert.Equal(TokenStatuses.Valid,      token.Status);
        Assert.Null(token.Parent); // non-user artifact: logout fan-outs keyed by subject never see it
        // RFC 7592 §5: the token stays valid while the client is registered - no expiry by default.
        Assert.Null(token.ExpireTime);

        var bound = JsonSerializer.Deserialize<RegistrationTokenPayload>(token.Payload!);
        Assert.Equal(response.ClientId, bound!.ClientId);
    }

    [Fact]
    public async Task Read_Back_With_A_Valid_Registration_Token() {
        var (handler, apps, tokens, _, _, securities) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            ClientName   = "RP",
        }, null, CancellationToken.None);
        apps.Setup(m => m.FindByClientIdAsync(created.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication {
                ClientId     = created.ClientId,
                Name         = "registered-resource",
                CanonicalName = "applications/registered-resource",
                ClientName   = "RP",
                RedirectUris = ["https://rp.example/cb"],
            });

        var read   = await handler.ReadAsync(created.ClientId, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(created.ClientId, read.ClientId);
        Assert.Equal("RP", read.ClientName);
    }
    [Fact]
    public async Task Echo_Registered_Jwks_On_Read_Back() {
        var (handler, apps, tokens, _, _, securities) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            Jwks         = JsonDocument.Parse("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""").RootElement.Clone(),
        }, null, CancellationToken.None);
        apps.Setup(m => m.FindByClientIdAsync(created.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication {
                ClientId     = created.ClientId,
                Name         = "registered-resource",
                CanonicalName = "applications/registered-resource",
                ClientName   = "RP",
                RedirectUris = ["https://rp.example/cb"],
            });

        var read   = await handler.ReadAsync(created.ClientId, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""", read.Jwks!.Value.GetRawText());
    }

    [Fact]
    public async Task Reject_Read_Back_With_A_Foreign_Token() {
        var (handler, apps, tokens, _, _, securities) = Create(handler: new StubHandler("[]"));
        var other = await handler.HandleAsync(new() {
            RedirectUris = ["https://other.example/cb"],
        }, null, CancellationToken.None);
        var mine = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);
        apps.Setup(m => m.FindByClientIdAsync(mine.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = mine.ClientId });

        var read   = await handler.ReadAsync(mine.ClientId, other.RegistrationAccessToken, CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task Reject_Read_Back_With_An_Unknown_Token() {
        var (handler, apps, tokens, _, _, securities) = Create();

        var read   = await handler.ReadAsync("whatever", "not-a-known-token", CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task Reject_Read_Back_With_A_Malformed_Token_Payload() {
        var (handler, apps, tokens, store, _, securities) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);
        apps.Setup(m => m.FindByClientIdAsync(created.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = created.ClientId });

        store.Single().Payload = "not-json";

        var read   = await handler.ReadAsync(created.ClientId, created.RegistrationAccessToken, CancellationToken.None);

        Assert.Null(read);
    }
    [Fact]
    public async Task Reject_A_Malformed_Software_Statement() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris      = ["https://rp.example/cb"],
                SoftwareStatement = "not-a-jwt",
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidSoftwareStatement, ex.Status);
    }

    [Fact]
    public async Task Reject_An_Unapproved_Software_Statement() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new() {
                RedirectUris      = ["https://rp.example/cb"],
                SoftwareStatement = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJzb2Z0d2FyZSJ9.c2ln",
            }, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.UnapprovedSoftwareStatement, ex.Status);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("some-token")]
    public async Task Reject_A_Registration_Without_A_Host_Validator_With_401(string? bearerToken) {
        var (_, apps, tokens, _, options, _) = Create();

        var denyAll = new RegisterHandler<SchemataApplication>(
            apps.Object, tokens.Object, TestSecurityKeys.CreateTokenService(options),
            Options.Create(options), new Mock<IHttpClientFactory>().Object,
            new Mock<ISecurityStore<SchemataSecurity>>().Object, new Mock<ISecretVerifier>().Object);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => denyAll.HandleAsync(new() { RedirectUris = ["https://rp.example/cb"] }, bearerToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidToken, ex.Status);
        Assert.Equal(401, ex.Code);
    }

    [Fact]
    public async Task Invoke_The_Request_Advisor_Exactly_Once_On_Create_And_On_Replace() {
        var (handler, _, _, _, _, _) = Create();
        var advisor  = new CountingRequestAdvisor();
        var services = new ServiceCollection();
        services.AddSingleton<IRegistrationRequestAdvisor<SchemataApplication>>(advisor);
        using var sp      = services.BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(sp));

        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);
        Assert.Equal(1, advisor.Calls);

        var replaced = await handler.ReplaceAsync(created.ClientId, new() {
            ClientId     = created.ClientId,
            RedirectUris = ["https://rp.example/cb"],
        }, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Equal(2, advisor.Calls);
    }

    [Fact]
    public async Task Apply_A_Trusted_Statement_On_Replace_With_Exact_Echo() {
        var validator = new Mock<ISoftwareStatementValidator>();
        validator.Setup(v => v.ValidateAndExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(SoftwareStatementValidationResult.Approved(new Dictionary<string, JsonElement> {
                     ["client_name"]        = JsonSerializer.Deserialize<JsonElement>("\"Trusted RP\""),
                     ["client_id"]          = JsonSerializer.Deserialize<JsonElement>("\"forged-id\""),
                     ["software_statement"] = JsonSerializer.Deserialize<JsonElement>("\"forged-statement\""),
                 }));
        var (handler, _, _, _, _, _) = Create(softwareStatements: validator.Object);

        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            ClientName   = "Body Name",
        }, null, CancellationToken.None);

        var replaced = await handler.ReplaceAsync(created.ClientId, new() {
            ClientId          = created.ClientId,
            RedirectUris      = ["https://rp.example/cb"],
            ClientName        = "Replaced Body",
            SoftwareStatement = Statement,
        }, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Equal("Trusted RP", replaced.ClientName);
        Assert.Equal(created.ClientId, replaced.ClientId);
        Assert.Equal(Statement, replaced.SoftwareStatement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Distinguish_Invalid_Statement_From_Unapproved_Statement(bool invalid) {
        var validator = new Mock<ISoftwareStatementValidator>();
        validator.Setup(v => v.ValidateAndExtractAsync(Statement, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalid ? SoftwareStatementValidationResult.Invalid : SoftwareStatementValidationResult.Unapproved);
        var (handler, _, _, _, _, _) = Create(softwareStatements: validator.Object);
        var error = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"], SoftwareStatement = Statement,
        }, null, CancellationToken.None));
        Assert.Equal(invalid ? OAuthErrors.InvalidSoftwareStatement : OAuthErrors.UnapprovedSoftwareStatement, error.Status);
        Assert.Empty(_registered);
    }

    [Fact]
    public async Task Reject_Wrongly_Typed_Approved_Metadata_Before_Persistence() {
        var validator = new Mock<ISoftwareStatementValidator>();
        validator.Setup(v => v.ValidateAndExtractAsync(Statement, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SoftwareStatementValidationResult.Approved(new Dictionary<string, JsonElement> {
                ["client_name"] = JsonSerializer.Deserialize<JsonElement>("{\"value\":\"invalid-shape\"}"),
            }));
        var (handler, _, _, _, _, _) = Create(softwareStatements: validator.Object);
        var error = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"], SoftwareStatement = Statement,
        }, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
        Assert.Empty(_registered);
    }

    [Fact]
    public async Task Apply_Approved_Statement_On_Create_Without_Importing_Server_Authority() {
        var validator = new Mock<ISoftwareStatementValidator>();
        validator.Setup(v => v.ValidateAndExtractAsync(Statement, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SoftwareStatementValidationResult.Approved(new Dictionary<string, JsonElement> {
                ["client_name"] = JsonSerializer.Deserialize<JsonElement>("\"Trusted RP\""),
                ["client_id"] = JsonSerializer.Deserialize<JsonElement>("\"forged\""),
                ["client_secret"] = JsonSerializer.Deserialize<JsonElement>("\"forged-secret\""),
                ["permissions"] = JsonSerializer.Deserialize<JsonElement>("[\"e:token\"]"),
            }));
        var (handler, _, _, _, _, _) = Create(softwareStatements: validator.Object);
        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"], ClientName = "Plain", SoftwareStatement = Statement,
        }, null, CancellationToken.None);
        Assert.Equal("Trusted RP", response.ClientName);
        Assert.Equal(Statement, response.SoftwareStatement);
        Assert.NotEqual("forged", response.ClientId);
        Assert.NotEqual("forged-secret", response.ClientSecret);
        Assert.Empty(Assert.Single(_registered).Permissions ?? []);
    }

    [Fact]
    public async Task Reject_A_Malformed_Statement_On_Replace() {
        var (handler, _, _, _, _, _) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ReplaceAsync(created.ClientId, new() {
            ClientId          = created.ClientId,
            RedirectUris      = ["https://rp.example/cb"],
            SoftwareStatement = "not-a-jwt",
        }, created.RegistrationAccessToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidSoftwareStatement, ex.Status);
    }

    [Fact]
    public async Task Reject_An_Unapproved_Statement_On_Replace() {
        var validator = new Mock<ISoftwareStatementValidator>();
        validator.Setup(v => v.ValidateAndExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(SoftwareStatementValidationResult.Unapproved);
        var (handler, _, _, _, _, _) = Create(softwareStatements: validator.Object);
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ReplaceAsync(created.ClientId, new() {
            ClientId          = created.ClientId,
            RedirectUris      = ["https://rp.example/cb"],
            SoftwareStatement = Statement,
        }, created.RegistrationAccessToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.UnapprovedSoftwareStatement, ex.Status);
    }

    [Fact]
    public async Task Add_And_Clear_Par_And_Jar_Metadata_On_Replace() {
        var (handler, _, _, _, _, _) = Create();
        var jarOptions = new JwtSecuredAuthorizationRequestsOptions();
        jarOptions.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
        var par = new AdviceRegistrationPushedAuthorizationRequests<SchemataApplication>();
        var jar = new AdviceRegistrationJwtSecuredAuthorizationRequests<SchemataApplication>(Options.Create(jarOptions));
        var services = new ServiceCollection();
        services.AddSingleton<IRegistrationRequestAdvisor<SchemataApplication>>(par);
        services.AddSingleton<IRegistrationRequestAdvisor<SchemataApplication>>(jar);
        services.AddSingleton<IRegistrationResponseAdvisor<SchemataApplication>>(par);
        services.AddSingleton<IRegistrationResponseAdvisor<SchemataApplication>>(jar);
        using var sp      = services.BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(sp));

        var created = await handler.HandleAsync(new() {
            RedirectUris                       = ["https://rp.example/cb"],
            RequirePushedAuthorizationRequests = true,
            RequestObjectSigningAlg            = SigningAlgorithms.RsaSha256,
        }, null, CancellationToken.None);

        Assert.True(created.RequirePushedAuthorizationRequests);
        Assert.Equal(SigningAlgorithms.RsaSha256, created.RequestObjectSigningAlg);

        var replaced = await handler.ReplaceAsync(created.ClientId, new() {
            ClientId     = created.ClientId,
            RedirectUris = ["https://rp.example/cb"],
        }, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Null(replaced.RequirePushedAuthorizationRequests);
        Assert.Null(replaced.RequestObjectSigningAlg);
    }

    [Fact]
    public async Task Reject_An_Unsupported_Request_Object_Algorithm_On_Replace() {
        var (handler, _, _, _, _, _) = Create();
        var jarOptions = new JwtSecuredAuthorizationRequestsOptions();
        jarOptions.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
        var services = new ServiceCollection();
        services.AddSingleton<IRegistrationRequestAdvisor<SchemataApplication>>(
            new AdviceRegistrationJwtSecuredAuthorizationRequests<SchemataApplication>(Options.Create(jarOptions)));
        using var sp      = services.BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(sp));

        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ReplaceAsync(created.ClientId, new() {
            ClientId                = created.ClientId,
            RedirectUris            = ["https://rp.example/cb"],
            RequestObjectSigningAlg = SigningAlgorithms.EcdsaSha256,
        }, created.RegistrationAccessToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Reject_Server_Managed_Fields_On_Replace() {
        var (handler, _, _, _, _, _) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ReplaceAsync(created.ClientId, new() {
            ClientId              = created.ClientId,
            RedirectUris          = ["https://rp.example/cb"],
            RegistrationClientUri = "https://forged.example/register",
        }, created.RegistrationAccessToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task Clear_Omitted_Metadata_On_Replace() {
        var (handler, _, _, _, options, securities) = Create();
        TestSecurityKeys.AddSigningRow(securities.Object, options.Issuer!);
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            ClientName   = "RP",
            Contacts     = ["ops@rp.example"],
            Scope        = "openid profile",
        }, null, CancellationToken.None);

        var replaced = await handler.ReplaceAsync(created.ClientId, new() {
            ClientId     = created.ClientId,
            RedirectUris = ["https://rp.example/cb"],
        }, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Null(replaced.ClientName);
        Assert.Null(replaced.Contacts);
        Assert.Null(replaced.Scope);
    }

    [Fact]
    public async Task Register_An_OAuth_Only_Client_Without_Redirect_Or_Code_Defaults() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            GrantTypes = [GrantTypes.ClientCredentials],
        }, null, CancellationToken.None);

        Assert.NotNull(response.ClientId);
        Assert.Equal([GrantTypes.ClientCredentials], response.GrantTypes);
        Assert.Null(response.ResponseTypes);
        Assert.Null(response.RedirectUris);
    }

    [Fact]
    public async Task Rotate_The_Registration_Token_On_Read() {
        var (handler, _, _, store, _, _) = Create(configure: o => o.RotateRegistrationTokens = true);
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);
        var rat1 = created.RegistrationAccessToken!;

        var read = await handler.ReadAsync(created.ClientId, rat1, CancellationToken.None);

        Assert.NotNull(read);
        var rat2 = read.RegistrationAccessToken;
        Assert.NotNull(rat2);
        Assert.NotEqual(rat1, rat2);

        // The predecessor is redeemed by the same store operation that publishes the successor.
        Assert.Equal(TokenStatuses.Redeemed, store.Single(t => t.ReferenceId == rat1).Status);
        var successor = store.Single(t => t.ReferenceId == rat2);
        Assert.Equal(TokenStatuses.Valid, successor.Status);
        // RFC 7592 §5: a null (non-expiring) lifetime carries over to the successor unchanged.
        Assert.Null(successor.ExpireTime);

        // The retired predecessor no longer authorizes; reading with the successor rotates again,
        // still leaving exactly one live registration token.
        Assert.Null(await handler.ReadAsync(created.ClientId, rat1, CancellationToken.None));

        var third = await handler.ReadAsync(created.ClientId, rat2, CancellationToken.None);

        Assert.NotNull(third);
        Assert.Single(store, t => t.Type == TokenTypes.Registration && t.Status == TokenStatuses.Valid);
    }

    [Fact]
    public async Task Return_Null_When_The_Rotation_Is_Lost_On_Read() {
        var (handler, _, tokens, store, _, _) = Create(configure: o => o.RotateRegistrationTokens = true);
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);
        var rat1 = created.RegistrationAccessToken!;

        // A concurrent management request won the rotation: the predecessor moved and the loser
        // publishes nothing.
        tokens.Setup(m => m.TryRotateAsync(
                   It.IsAny<SchemataToken>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(), It.IsAny<CancellationToken>(),
                   It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .Callback<SchemataToken, IReadOnlyCollection<SchemataToken>, CancellationToken, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>(
                (predecessor, _, _, _) => predecessor.Status = TokenStatuses.Redeemed)
            .ReturnsAsync(false);

        var read = await handler.ReadAsync(created.ClientId, rat1, CancellationToken.None);

        // The loser never receives the now-redeemed predecessor as if it were usable.
        Assert.Null(read);
        Assert.Single(store);
        Assert.DoesNotContain(store, t => t.Status == TokenStatuses.Valid);
    }

    [Fact]
    public async Task Return_Null_When_The_Rotation_Is_Lost_On_Replace() {
        var (handler, _, tokens, store, _, _) = Create(configure: o => o.RotateRegistrationTokens = true);
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        tokens.Setup(m => m.TryRotateAsync(
                   It.IsAny<SchemataToken>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(), It.IsAny<CancellationToken>(),
                   It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .Callback<SchemataToken, IReadOnlyCollection<SchemataToken>, CancellationToken, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>(
                (predecessor, _, _, _) => predecessor.Status = TokenStatuses.Redeemed)
            .ReturnsAsync(false);

        var replaced = await handler.ReplaceAsync(created.ClientId, new() {
            ClientId     = created.ClientId,
            RedirectUris = ["https://rp.example/cb"],
        }, created.RegistrationAccessToken, CancellationToken.None);

        Assert.Null(replaced);
        Assert.Single(store);
        Assert.DoesNotContain(store, t => t.Status == TokenStatuses.Valid);
    }

    [Fact]
    public async Task Apply_A_Finite_Lifetime_To_Initial_And_Rotated_Tokens() {
        var clock = new FakeTimeProvider(new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var (handler, _, _, store, _, _) = Create(
            configure: o => {
                o.RegistrationTokenLifetime = TimeSpan.FromHours(2);
                o.RotateRegistrationTokens  = true;
            },
            time: clock);

        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        var initial = store.Single(t => t.ReferenceId == created.RegistrationAccessToken);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddHours(2), initial.ExpireTime);

        var read = await handler.ReadAsync(created.ClientId, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(read);
        var successor = store.Single(t => t.ReferenceId == read.RegistrationAccessToken);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddHours(2), successor.ExpireTime);
    }

    [Fact]
    public async Task Reject_Read_With_Expired_Or_Redeemed_Predecessor() {
        var clock = new FakeTimeProvider(new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var (expiring, _, _, _, _, _) = Create(
            configure: o => o.RegistrationTokenLifetime = TimeSpan.FromHours(1),
            time: clock);
        var created = await expiring.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await expiring.ReadAsync(created.ClientId, created.RegistrationAccessToken, CancellationToken.None));

        var (handler, _, _, store, _, _) = Create();
        var other = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);
        store.Single().Status = TokenStatuses.Redeemed;

        Assert.Null(await handler.ReadAsync(other.ClientId, other.RegistrationAccessToken, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Fails_Closed_When_The_Application_Delete_Loses_Concurrency() {
        var (handler, apps, tokens, store, _, _) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
        }, null, CancellationToken.None);

        apps.Setup(m => m.DeleteAsync(It.IsAny<SchemataApplication>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AbortedException());

        var deleted = await handler.DeleteAsync(created.ClientId, created.RegistrationAccessToken, CancellationToken.None);

        Assert.False(deleted);
        Assert.Single(_registered);

        Assert.All(store, t => Assert.Equal(TokenStatuses.Valid, t.Status));
        Assert.All(_rows, row => Assert.Equal(SecurityConstants.Statuses.Valid, row.Status));
        tokens.Verify(
            m => m.RevokeByApplicationAsync("applications/registered-resource", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Replace_Key_Material_Only_After_The_Listing_Streams_Close() {
        var (handler, _, _, _, _, securities) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            Jwks         = JsonDocument.Parse("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""").RootElement.Clone(),
        }, null, CancellationToken.None);

        // Both kind listings share the repository context with the updates: no row may be
        // mutated while either stream is open.
        var jwks    = new ListingSpy(_rows, "applications/registered-resource", kind: SecurityConstants.Kinds.Jwks);
        var jwksUri = new ListingSpy(_rows, "applications/registered-resource", kind: SecurityConstants.Kinds.JwksUri);
        securities.Setup(s => s.ListByParentAsync(
                       "applications/registered-resource", SecurityConstants.Kinds.Jwks, null, null,
                       It.IsAny<CancellationToken>()))
                  .Returns(jwks);
        securities.Setup(s => s.ListByParentAsync(
                       "applications/registered-resource", SecurityConstants.Kinds.JwksUri, null, null,
                       It.IsAny<CancellationToken>()))
                  .Returns(jwksUri);
        var updatedWhileOpen = false;
        securities.Setup(s => s.UpdateAsync(It.IsAny<SchemataSecurity>(), It.IsAny<CancellationToken>(),
                                             It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
                  .Callback((SchemataSecurity _, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => {
                      if (jwks.Open || jwksUri.Open) {
                          updatedWhileOpen = true;
                      }
                  })
                  .Returns(Task.CompletedTask);

        var replaced = await handler.ReplaceAsync(created.ClientId, new() {
            ClientId     = created.ClientId,
            RedirectUris = ["https://rp.example/cb"],
            Jwks         = JsonDocument.Parse("""{"keys":[{"kty":"RSA","kid":"rp-2","use":"sig","n":"y","e":"AQAB"}]}""").RootElement.Clone(),
        }, created.RegistrationAccessToken, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.False(updatedWhileOpen);

        var keyRows = _rows.Where(row => row.Kind == SecurityConstants.Kinds.Jwks).ToList();
        Assert.Equal(2, keyRows.Count);
        Assert.Equal(SecurityConstants.Statuses.Revoked, Assert.Single(keyRows, row => row.Value!.Contains("rp-1")).Status);
        Assert.Equal(SecurityConstants.Statuses.Valid, Assert.Single(keyRows, row => row.Value!.Contains("rp-2")).Status);
    }

    private const string Statement = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJzb2Z0d2FyZSJ9.c2ln";

    private sealed class CountingRequestAdvisor : IRegistrationRequestAdvisor<SchemataApplication>
    {
        public int Calls;

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext       ctx,
            RegisterRequest     request,
            SchemataApplication application,
            CancellationToken   ct = default
        ) {
            Calls++;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class ListingSpy(
        List<SchemataSecurity> rows,
        string?                parent,
        string?                kind   = null,
        string?                status = null
    ) : IAsyncEnumerable<SchemataSecurity>
    {
        public bool Open { get; private set; }

        public async IAsyncEnumerator<SchemataSecurity> GetAsyncEnumerator(
            CancellationToken ct = default
        ) {
            Open = true;
            try {
                foreach (var row in rows.Where(row => row.Parent == parent
                                                   && (kind is null || row.Kind == kind)
                                                   && (status is null || row.Status == status))) {
                    yield return row;
                }
            } finally {
                Open = false;
            }
        }
    }

}
