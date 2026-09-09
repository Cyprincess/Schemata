using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RequestObjectReaderShould
{
    private const string ClientId = "client-1";
    private const string Issuer   = "https://as.example";
    private const string KeyId    = "jar-key";

    private const string PrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQDBTxeLz7f8TEZh
        +179s+XfPvuPHP5FEi97sYVEvc7W5Mwa8VoalLTD7wDhZ/FEkl7XqFKhYntUe8oX
        1YEF9bOVDD7Lqvka+OIbNJEctoDzSNapi9tFq4xCWzmg0BdaKNCf4r8C5TRcc8K1
        W7UXmZO4oLyn03CEmo7jberXWNuJvrAetvbC0Cf80a9N1T4LnwMyE3EpAFrmWbJs
        V+xq3vtws//oXMwkqE+ayMYihcYQH6gmgjF8iTC9fbnJOwuxYam2q9fHoNJAXna+
        7uThi1PAQW2yZtEfLxFSsZ6A6ifEFyfGSZDact3dz7yZf24rdpGrSjabliznQnaH
        JT1JVkJXAgMBAAECggEAC7C8urq9gA5T9wrVuLta7GvXT3nZdwG9nBoJv2va38MU
        3/2tll5c4RRAo7x+jtpq8utnGifcvV+Ui9cpq7cY/NAM+0jkDd4GmDwqhrnufFEn
        t1wtPKLi4zsA9h3K874ZJhWb3lEHMmfZf4dD6nNiIvks9mvd/DFA8RFf3zDRIgqF
        +Mux8XXqHQzKj6qTOE6u3zGzA0XcGg7ROXBz1e1jt2JanaWuSZkyGzigns6GrP5q
        xo10FDQMCFueCq/OgA8pWQwC9Onefcm9U+4nh+4S4VX070rvttfiVzN1nG9Pa7wu
        BkXbPobN0k25Knz5cbpnWcuX0Ysa4j+vFnb/MfsDkQKBgQDvQDM6msPRsTgrVEir
        A/2JkFyid7sGADV/KZjBzBDg17HN5wqTsO3bOCrctd4kvr0yPAbSok7NIlzqxP6z
        pCF7MVJ1z3INGwB31k7kxPS4o3J82fp9f3Np4okh/kA904BJNnmTivU3fl5AFeDP
        I+sacJfw8Du6W/DPKpuUmt5khwKBgQDO14hS8xZq94oq9gBpvq5B37jexLPknvBb
        PFOvw/QvbeX20hSzfY58m4oRv2yaizFbrgl2ujgoxhAO5rMNKpVWilrYUfU9Jt2U
        duTcwGGphbdAHsbF5qJwBH34xyB1SfM4AFM3Ne/j4CmWq1pXyRu3jL2efOvijkDl
        7WlVkmh3sQKBgDI8d8VJc+6W2KV4eB24o8b70genPlT/PDxsPpAKykx73fDPH5Bt
        aTRLsexQH8n1ZwKiSgh2XjeCjKIbJSQRRZp5e8gS++62ib2f+Ubd+EjAwSxoFMm/
        Nju4fnTtzw1sWJaG4rZRSjYRybaJIYA9GYOeurizqMbzwTeuyaZFcocHAoGBAJT3
        5dbX/G1NmtUWF3YRPq9y0VKfmHAviCwtZphQKK2AGP+Kjh43b0ePPgFKPI2Rchr1
        XRuFQq0a+LXCsXHqGWQagRMK8/T26N7kQjA63huZkkE76Szezl6e3ZsuztGqUOEk
        WsqIrh0QdONuxcIztSKc2nQqxDiG+3adZh+bMmIBAoGBAItIiPqVrpVMn/RET2IP
        QyJ6kXMdpvfWrNoMXDU+a6TPzxtyIcUC5zpn7ign9fqq6sn4z37aDeIwRQ1u63hG
        fgqE0qimqODEvG4bND+AcKB+RI2FCPLxMX6fcWhqDPBNrf8yLlnUKa/DXeIR88Gg
        VHBkmrb1tbyEdDFFs0Bmq2HQ
        -----END PRIVATE KEY-----
        """;

    [Fact]
    public async Task Accept_A_Valid_Signed_Object_And_Ignore_Duplicate_Query_Parameters() {
        var target = new AuthorizeRequest {
            ClientId     = ClientId,
            RedirectUri  = "https://query.example/callback",
            ResponseType = "code",
            Scope        = "openid query-scope",
            State        = "query-state",
            LoginHint    = "query@example.com",
        };
        var assertion = Mint(new() {
            [Parameters.ClientId]     = ClientId,
            [Parameters.RedirectUri]  = "https://object.example/callback",
            [Parameters.ResponseType] = "code",
            [Parameters.Scope]        = "openid object-scope",
            [Parameters.State]        = "object-state",
        });
        target.Request = assertion;

        await Reader().ReadAsync(assertion, Application(), target, true, CancellationToken.None);

        Assert.Equal(ClientId, target.ClientId);
        Assert.Equal("https://object.example/callback", target.RedirectUri);
        Assert.Equal("openid object-scope", target.Scope);
        Assert.Equal("object-state", target.State);
        Assert.Null(target.LoginHint);
        Assert.Equal(assertion, target.Request);
    }

    [Fact]
    public async Task Reject_An_Object_With_A_Tampered_Signature() {
        var assertion    = Mint(BaseClaims());
        var signatureAt  = assertion.LastIndexOf('.') + 1;
        var tamperedChar = assertion[signatureAt] == 'A' ? 'B' : 'A';
        var tampered     = assertion[..signatureAt] + tamperedChar + assertion[(signatureAt + 1)..];

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(tampered, Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Theory]
    [InlineData(Parameters.Request)]
    [InlineData(Parameters.RequestUri)]
    public async Task Reject_An_Object_Containing_A_Nested_Request_Parameter(string nested) {
        var claims = BaseClaims();
        claims[nested] = "nested-value";

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(Mint(claims), Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Object_Missing_Client_Id() {
        var claims = BaseClaims();
        claims.Remove(Parameters.ClientId);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(Mint(claims), Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Object_Whose_Client_Id_Differs_From_The_Query() {
        var claims = BaseClaims();
        claims[Parameters.ClientId] = "client-2";

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(Mint(claims), Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    [Fact]
    public async Task Reject_The_Client_Algorithm_When_The_Server_Does_Not_Allow_It() {
        var app = Application();
        app.RequestObjectSigningAlg = SigningAlgorithms.RsaSha256;
        var options = Options();
        options.SigningAlgorithms.Clear();
        options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha384);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader(options).ReadAsync(Mint(BaseClaims()), app, Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Unsigned_Object_When_Signed_Objects_Are_Required() {
        var options = Options();
        options.RequireForAllClients = true;
        var assertion = Unsigned(BaseClaims());

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader(options).ReadAsync(assertion, Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Oidc_Object_When_The_Query_Scope_Omits_Openid() {
        var target = Query();
        target.Scope = "profile";

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(Mint(BaseClaims()), Application(), target, true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Oidc_Object_When_The_Query_Response_Type_Differs() {
        var target = Query();
        target.ResponseType = "token";

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(Mint(BaseClaims()), Application(), target, true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Require_A_Request_Object_When_The_Server_Or_Client_Requires_It(
        bool serverRequires,
        bool clientRequires
    ) {
        var app = Application();
        app.RequireSignedRequestObject = clientRequires;
        var options = Options();
        options.RequireForAllClients = serverRequires;
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(value => value.FindByClientIdAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        var advisor = new AdviceAuthorizeRequestObject<SchemataApplication>(
            apps.Object,
            Reader(options),
            Microsoft.Extensions.Options.Options.Create(options));
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { ClientId = ClientId, ResponseType = "code", Scope = "openid" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    private static RequestObjectReader<SchemataApplication> Reader(JwtSecuredAuthorizationRequestsOptions? options = null) {
        var row = new SchemataSecurity {
            Uid       = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Parent    = "applications/" + ClientId,
            Name      = KeyId,
            Kind      = SecurityConstants.Kinds.Jwks,
            Usage     = SecurityConstants.Usages.Authentication,
            Status    = SecurityConstants.Statuses.Valid,
            Value     = Jwks(),
        };
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();
        securities.Setup(value => value.ListByParentAsync(
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<CancellationToken>()))
                  .Returns(Enumerate(row));
        var http = new Mock<IHttpClientFactory>();
        http.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        return new(
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer }),
            Microsoft.Extensions.Options.Options.Create(new SchemataSecurityOptions()),
            http.Object,
            cache.Object,
            securities.Object,
            new ClientAssertionChannel());
    }

    private static JwtSecuredAuthorizationRequestsOptions Options() {
        var options = new JwtSecuredAuthorizationRequestsOptions();
        options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
        return options;
    }

    private static SchemataApplication Application() {
        return new() {
            Uid                     = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClientId                = ClientId,
            CanonicalName           = "applications/" + ClientId,
            RequestObjectSigningAlg = SigningAlgorithms.RsaSha256,
        };
    }

    private static AuthorizeRequest Query() {
        return new() {
            ClientId     = ClientId,
            ResponseType = "code",
            Scope        = "openid profile",
            Request      = "request-object",
        };
    }

    private static Dictionary<string, object> BaseClaims() {
        return new() {
            [Parameters.ClientId]     = ClientId,
            [Parameters.ResponseType] = "code",
            [Parameters.Scope]        = "openid profile",
        };
    }

    private static string Mint(Dictionary<string, object> claims) {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKey);
        var key = new RsaSecurityKey(rsa) {
            KeyId = KeyId,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Claims             = claims,
            SigningCredentials = new(key, SigningAlgorithms.RsaSha256),
        });
    }

    private static string Unsigned(Dictionary<string, object> claims) {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Claims = claims });
    }

    private static string Jwks() {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKey);
        var parameters = rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new {
            keys = new[] {
                new {
                    kty = "RSA",
                    kid = KeyId,
                    use = "sig",
                    n   = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e   = Base64UrlEncoder.Encode(parameters.Exponent!),
                },
            },
        });
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(SchemataSecurity row) {
        await Task.Yield();
        yield return row;
    }
}
