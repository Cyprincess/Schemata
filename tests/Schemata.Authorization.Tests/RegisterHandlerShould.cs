using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RegisterHandlerShould
{
    private readonly List<SchemataSecurity> _rows = new();

    private (RegisterHandler<SchemataApplication> Handler, Mock<IApplicationManager<SchemataApplication>> Apps, Mock<ITokenStore<SchemataToken>> Tokens, List<SchemataToken> Store, SchemataAuthorizationOptions Options, Mock<ISecurityStore<SchemataSecurity>> Securities) Create(
        Action<SchemataAuthorizationOptions>? configure = null,
        HttpMessageHandler?                   handler   = null
    ) {
        var options = new SchemataAuthorizationOptions {
            Issuer = "https://as.example",
        };
        configure?.Invoke(options);

        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(m => m.CreateAsync(It.IsAny<SchemataApplication>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemataApplication a, CancellationToken _) => a);
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();
        securities
            .Setup(s => s.CreateAsync(It.IsAny<SchemataSecurity>(), It.IsAny<CancellationToken>()))
            .Callback<SchemataSecurity, CancellationToken>((row, _) => _rows.Add(row))
            .ReturnsAsync((SchemataSecurity row, CancellationToken _) => row);
        securities
            .Setup(s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string?, string?, string?, string?, CancellationToken>((parent, kind, _, _, _) =>
                Enumerate(_rows.Where(row => row.Parent == parent && (kind is null || row.Kind == kind))));

        var verifier = new Mock<ISecretVerifier>();
        verifier
            .Setup(v => v.HashAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string presented, string? _, CancellationToken _) => $"hashed:{presented}");

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        var stored = new List<SchemataToken>();
        tokens.Setup(m => m.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
            .Callback<SchemataToken, CancellationToken>((t, _) => stored.Add(t))
            .ReturnsAsync((SchemataToken t, CancellationToken _) => t);
        tokens.Setup(m => m.FindByReferenceIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? r, CancellationToken _) => stored.FirstOrDefault(t => t.ReferenceId == r));

        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler ?? new NotFoundHandler()) { Timeout = TimeSpan.FromSeconds(10) });

        var initialAccess = new Mock<IInitialAccessTokenValidator>();
        initialAccess.Setup(v => v.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(true);

        var registerHandler = new RegisterHandler<SchemataApplication>(
            apps.Object, tokens.Object, TestSecurityKeys.CreateTokenService(options),
            Options.Create(options), http.Object, securities.Object, verifier.Object,
            initialAccess: initialAccess.Object);

        return (registerHandler, apps, tokens, stored, options, securities);
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(IEnumerable<SchemataSecurity> rows) {
        foreach (var row in rows) {
            yield return row;
        }
    }

    [Fact]
    public async Task Register_A_Confidential_Web_Client_With_201_Fields() {
        var (handler, _, _, _, _, _) = Create();

        var response = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            ClientName   = "RP",
        }, null, CancellationToken.None);

        Assert.NotNull(response.ClientId);
        Assert.NotEmpty(response.ClientId);
        Assert.NotNull(response.ClientSecret);
        Assert.Equal(0, response.ClientSecretExpiresAt);
        Assert.NotNull(response.ClientIdIssuedAt);
        Assert.Equal("RP", response.ClientName);
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
        Assert.Equal($"applications/{response.ClientId}", row.Parent);
        Assert.Equal(response.ClientId, row.Name);
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
            Jwks         = """{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""",
        }, null, CancellationToken.None);

        var row = Assert.Single(_rows, value => value.Kind == SecurityConstants.Kinds.Jwks);
        Assert.Equal($"applications/{response.ClientId}", row.Parent);
        Assert.Equal(response.ClientId, row.Name);
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
        Assert.Equal($"applications/{response.ClientId}", row.Parent);
        Assert.Equal(response.ClientId, row.Name);
        Assert.Equal(SecurityConstants.Usages.Authentication, row.Usage);
        Assert.Equal(SecurityConstants.Statuses.Valid, row.Status);
        Assert.Equal("https://rp.example/jwks.json", row.Value);
    }

    [Fact]
    public async Task Reject_Missing_Redirect_Uris_With_Invalid_Redirect_Uri() {
        var (handler, _, _, _, _, _) = Create();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(new(), null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }

    [Theory]
    [InlineData("http://rp.example/cb",         ApplicationTypes.Web)]     // http on web
    [InlineData("https://127.0.0.1/cb",         ApplicationTypes.Web)]     // loopback on web
    [InlineData("https://rp.example/cb",        ApplicationTypes.Native)]  // https on native
    [InlineData("http://localhost/cb",          ApplicationTypes.Native)]  // localhost excluded per RFC 8252 §8.3
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
                Jwks         = """{"keys":[]}""",
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
        Assert.NotNull(token.ExpireTime);

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
                ClientName   = "RP",
                RedirectUris = ["https://rp.example/cb"],
            });

        var reader = new RegistrationReadHandler<SchemataApplication>(apps.Object, tokens.Object, securities.Object);
        var read   = await reader.HandleAsync(new(created.ClientId, created.RegistrationAccessToken), CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(created.ClientId, read.ClientId);
        Assert.Equal("RP", read.ClientName);
    }
    [Fact]
    public async Task Echo_Registered_Jwks_On_Read_Back() {
        var (handler, apps, tokens, _, _, securities) = Create();
        var created = await handler.HandleAsync(new() {
            RedirectUris = ["https://rp.example/cb"],
            Jwks         = """{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""",
        }, null, CancellationToken.None);
        apps.Setup(m => m.FindByClientIdAsync(created.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication {
                ClientId     = created.ClientId,
                ClientName   = "RP",
                RedirectUris = ["https://rp.example/cb"],
            });

        var reader = new RegistrationReadHandler<SchemataApplication>(apps.Object, tokens.Object, securities.Object);
        var read   = await reader.HandleAsync(new(created.ClientId, created.RegistrationAccessToken), CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""", read.Jwks);
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

        var reader = new RegistrationReadHandler<SchemataApplication>(apps.Object, tokens.Object, securities.Object);
        var read   = await reader.HandleAsync(new(mine.ClientId, other.RegistrationAccessToken), CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task Reject_Read_Back_With_An_Unknown_Token() {
        var (handler, apps, tokens, _, _, securities) = Create();

        var reader = new RegistrationReadHandler<SchemataApplication>(apps.Object, tokens.Object, securities.Object);
        var read   = await reader.HandleAsync(new("whatever", "not-a-known-token"), CancellationToken.None);

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

        var reader = new RegistrationReadHandler<SchemataApplication>(apps.Object, tokens.Object, securities.Object);
        var read   = await reader.HandleAsync(new(created.ClientId, created.RegistrationAccessToken), CancellationToken.None);

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
            Options.Create(options),
            new Mock<IHttpClientFactory>().Object,
            new Mock<ISecurityStore<SchemataSecurity>>().Object, new Mock<ISecretVerifier>().Object);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => denyAll.HandleAsync(new() { RedirectUris = ["https://rp.example/cb"] }, bearerToken, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidToken, ex.Status);
        Assert.Equal(401, ex.Code);
    }

}
