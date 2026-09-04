using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class PrivateKeyJwtShould
{
    private const string Issuer   = "https://as.example";
    private const string ClientId = "client-1";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly List<SchemataSecurity> _rows = new();

    [Fact]
    public async Task Authenticate_An_Rsa_Assertion_Against_The_Registered_Jwks() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app);

        var assertion = Mint(key, SigningAlgorithms.RsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Authenticate_An_Ec_Assertion_Against_The_Registered_Jwks() {
        var key = EcKey("ec-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app);

        var assertion = Mint(key, SigningAlgorithms.EcdsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Select_The_Registered_Key_Matching_The_Assertion_Kid() {
        var first  = RsaKey("rsa-1");
        var second = RsaKey("rsa-2");
        var app    = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(first), Jwk(second))));
        var subject = CreateSubject(app);

        var assertion = Mint(second, SigningAlgorithms.RsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Authenticate_When_The_Registered_Algorithm_Matches_The_Assertion() {
        var key = EcKey("ec-1");
        var app = CreateApp();
        app.TokenEndpointAuthSigningAlg = SigningAlgorithms.EcdsaSha256;
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app);

        var assertion = Mint(key, SigningAlgorithms.EcdsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Reject_A_Multi_Key_Set_Without_An_Assertion_Kid() {
        var first  = RsaKey("rsa-1");
        var second = RsaKey("rsa-2");
        var app    = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(first), Jwk(second))));
        var subject = CreateSubject(app);

        var assertion = Mint(new RsaSecurityKey(RSA.Create(2048)), SigningAlgorithms.RsaSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_Kid_Matching_No_Registered_Key() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app);

        var assertion = Mint(RsaKey("rsa-9"), SigningAlgorithms.RsaSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_Signed_By_An_Unregistered_Key() {
        var registered = RsaKey("rsa-1");
        var app        = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(registered))));
        var subject = CreateSubject(app);

        var assertion = Mint(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "rsa-1" }, SigningAlgorithms.RsaSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }

    [Fact]
    public async Task Not_Poison_The_Replay_Slots_When_A_Forged_Assertion_Is_Rejected() {
        var registered = RsaKey("rsa-1");
        var app        = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(registered))));
        var burned = false;
        var slots  = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(
                 value => value.GetOrCreateAsync(
                     It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                     It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan _, CancellationToken _) => {
                if (burned) {
                    return new() { Value = "pre-existing" };
                }

                burned = true;
                return new() { Parent = parent, Provider = provider, Name = name, Value = marker };
            });
        var subject = CreateSubject(app, assertions: new(slots.Object, new FakeTimeProvider(Now)));

        var jti    = Guid.NewGuid().ToString();
        var forged = Mint(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "rsa-1" }, SigningAlgorithms.RsaSha256, jti: jti);
        var legit  = Mint(registered, SigningAlgorithms.RsaSha256, jti: jti);

        await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(forged), null, default));

        Assert.Same(app, await subject.AuthenticateAsync(null, Form(legit), null, default));
    }
    [Fact]
    public async Task Narrow_The_Algorithm_Allow_List_To_The_Registered_Signing_Algorithm() {
        var key = EcKey("ec-1");
        var app = CreateApp();
        app.TokenEndpointAuthSigningAlg = SigningAlgorithms.RsaSha256;
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app);

        var assertion = Mint(key, SigningAlgorithms.EcdsaSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_When_No_Key_Row_Is_Registered() {
        var app     = CreateApp();
        var subject = CreateSubject(app);

        var assertion = Mint(RsaKey("rsa-1"), SigningAlgorithms.RsaSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Verify_An_Assertion_Against_A_Retired_Key_Row() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key)), SecurityConstants.Statuses.Retired));
        var subject = CreateSubject(app);

        var assertion = Mint(key, SigningAlgorithms.RsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Reject_An_Assertion_When_The_Key_Row_Is_Revoked() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key)), SecurityConstants.Statuses.Revoked));
        var subject = CreateSubject(app);

        var assertion = Mint(key, SigningAlgorithms.RsaSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Resolve_A_Registered_Jwks_Uri_Row() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksUriRow("https://rp.example/jwks"));
        var subject = CreateSubject(app, remoteJwks: Jwks(Jwk(key)));

        var assertion = Mint(key, SigningAlgorithms.RsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Return_Null_When_The_Method_Is_Not_Allowed() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app, new() { Issuer = Issuer });

        var assertion = Mint(key, SigningAlgorithms.RsaSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Null(result);
    }
    [Fact]
    public async Task Return_Null_When_No_Assertion_Is_Presented() {
        var key = RsaKey("rsa-1");
        var app = CreateApp();
        _rows.Add(JwksRow(Jwks(Jwk(key))));
        var subject = CreateSubject(app);

        Assert.Null(await subject.AuthenticateAsync(null, null, null, default));
        Assert.Null(await subject.AuthenticateAsync(
            null,
            new() { [Parameters.ClientId] = [ClientId] },
            null,
            default));
    }

    private static SchemataApplication CreateApp() {
        return new() {
            Uid        = Guid.NewGuid(),
            ClientId   = ClientId,
            ClientType = ClientTypes.Confidential,
        };
    }

    private SchemataSecurity JwksRow(string jwks, string? status = null) {
        return new() {
            Uid    = Guid.NewGuid(),
            Parent = SecurityParents.Application(new() { ClientId = ClientId }),
            Name   = ClientId,
            Kind   = SecurityConstants.Kinds.Jwks,
            Usage  = SecurityConstants.Usages.Authentication,
            Value  = jwks,
            Status = status ?? SecurityConstants.Statuses.Valid,
        };
    }

    private SchemataSecurity JwksUriRow(string uri) {
        return new() {
            Uid    = Guid.NewGuid(),
            Parent = SecurityParents.Application(new() { ClientId = ClientId }),
            Name   = ClientId,
            Kind   = SecurityConstants.Kinds.JwksUri,
            Usage  = SecurityConstants.Usages.Authentication,
            Value  = uri,
            Status = SecurityConstants.Statuses.Valid,
        };
    }

    private PrivateKeyJwtAuthentication<SchemataApplication> CreateSubject(
        SchemataApplication           app,
        SchemataAuthorizationOptions? options    = null,
        ClientAssertionValidator?     assertions = null,
        string?                       remoteJwks = null
    ) {
        options ??= AllowedOptions();

        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByClientIdAsync(app.ClientId!, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var securities = new Mock<ISecurityStore<SchemataSecurity>>();
        securities
            .Setup(s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerate(_rows));

        var cache = new Mock<ICacheProvider>();
        cache
            .Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StubHandler(remoteJwks ?? """{"keys":[]}""")));

        return new(
            manager.Object,
            Options.Create(options),
            http.Object,
            cache.Object,
            Options.Create(new SchemataSecurityOptions()),
            securities.Object,
            assertions ?? Validator(),
            new());
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(IEnumerable<SchemataSecurity> rows) {
        foreach (var row in rows) {
            yield return row;
        }
    }

    private static SchemataAuthorizationOptions AllowedOptions() {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        options.AllowedClientAuthMethods.Add(ClientAuthMethods.PrivateKeyJwt);
        return options;
    }

    private static ClientAssertionValidator Validator() {
        var slots = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(
                 value => value.GetOrCreateAsync(
                     It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                     It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan _, CancellationToken _) =>
                new() { Parent = parent, Provider = provider, Name = name, Value = marker });

        return new(slots.Object, new FakeTimeProvider(Now));
    }

    private static Dictionary<string, List<string?>> Form(string assertion) {
        return new() {
            [Parameters.ClientAssertionType] = [ClientAssertionTypes.JwtBearer],
            [Parameters.ClientAssertion]     = [assertion],
        };
    }

    private static RsaSecurityKey RsaKey(string kid) {
        return new(RSA.Create(2048)) { KeyId = kid };
    }

    private static ECDsaSecurityKey EcKey(string kid) {
        return new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };
    }

    private static string Jwks(params object[] keys) {
        return "{\"keys\":[" + string.Join(",", keys.Select(key => JsonSerializer.Serialize(key))) + "]}";
    }

    private static object Jwk(RsaSecurityKey key) {
        var parameters = key.Rsa.ExportParameters(false);
        return new {
            kty = "RSA",
            kid = key.KeyId,
            use = "sig",
            n   = Base64UrlEncoder.Encode(parameters.Modulus!),
            e   = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
    }

    private static object Jwk(ECDsaSecurityKey key) {
        var parameters = key.ECDsa.ExportParameters(false);
        return new {
            kty = "EC",
            kid = key.KeyId,
            use = "sig",
            crv = "P-256",
            x   = Base64UrlEncoder.Encode(parameters.Q.X!),
            y   = Base64UrlEncoder.Encode(parameters.Q.Y!),
        };
    }

    private static string Mint(SecurityKey key, string algorithm, string? jti = null) {
        var descriptor = new SecurityTokenDescriptor {
            Issuer  = ClientId,
            Claims  = new Dictionary<string, object> {
                ["sub"] = ClientId,
                ["aud"] = new[] { Issuer + Endpoints.Token },
                ["jti"] = jti ?? Guid.NewGuid().ToString(),
            },
            Expires            = Now.AddMinutes(5).UtcDateTime,
            NotBefore          = Now.AddMinutes(-1).UtcDateTime,
            IssuedAt           = Now.AddMinutes(-1).UtcDateTime,
            SigningCredentials = new(key, algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
