using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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

public class ClientSecretJwtShould
{
    private const string Issuer   = "https://as.example";
    private const string ClientId = "client-1";
    private const string Secret   = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly List<SchemataSecurity> _rows = new();

    [Theory]
    [InlineData(SigningAlgorithms.HmacSha256)]
    [InlineData(SigningAlgorithms.HmacSha384)]
    [InlineData(SigningAlgorithms.HmacSha512)]
    public async Task Authenticate_An_Hs_Assertion_Signed_With_The_Registered_Secret_Row(string algorithm) {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key(Secret), algorithm);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Authenticate_When_Form_Client_Id_Matches_The_Assertion() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion, ClientId), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Reject_An_Assertion_Signed_With_A_Different_Secret() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"), SigningAlgorithms.HmacSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }

    [Fact]
    public async Task Not_Poison_The_Replay_Slots_When_A_Forged_Assertion_Is_Rejected() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions(), new(ReplaySlots().Object, new FakeTimeProvider(Now)));

        var jti    = Guid.NewGuid().ToString();
        var forged = Mint(Key("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"), SigningAlgorithms.HmacSha256, jti: jti);
        var legit  = Mint(Key(Secret), SigningAlgorithms.HmacSha256, jti: jti);

        await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(forged), null, default));

        Assert.Same(app, await subject.AuthenticateAsync(null, Form(legit), null, default));
    }
    [Fact]
    public async Task Reject_A_Replayed_Assertion_Whose_Signature_Verified() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions(), new(ReplaySlots().Object, new FakeTimeProvider(Now)));

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256);

        Assert.Same(app, await subject.AuthenticateAsync(null, Form(assertion), null, default));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_When_No_Secret_Row_Is_Registered() {
        var app     = CreateApp();
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"), SigningAlgorithms.HmacSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Select_The_Secret_Row_Matching_The_Assertion_Kid() {
        var app = CreateApp();
        _rows.Add(SecretRow(kid: "s-1"));
        _rows.Add(SecretRow(kid: "s-2", value: "9999999999999999999999999999999999999999999999999999999999999999"));
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(new(Encoding.UTF8.GetBytes(Secret)) { KeyId = "s-1" }, SigningAlgorithms.HmacSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Same(app, result);
    }
    [Fact]
    public async Task Reject_A_Multi_Secret_Registration_Without_An_Assertion_Kid() {
        var app = CreateApp();
        _rows.Add(SecretRow(kid: "s-1"));
        _rows.Add(SecretRow(kid: "s-2", value: "9999999999999999999999999999999999999999999999999999999999999999"));
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_Whose_Kid_Matches_No_Secret_Row() {
        var app = CreateApp();
        _rows.Add(SecretRow(kid: "s-1"));
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(new(Encoding.UTF8.GetBytes(Secret)) { KeyId = "s-9" }, SigningAlgorithms.HmacSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_When_The_Secret_Row_Is_Revoked() {
        var app = CreateApp();
        _rows.Add(SecretRow(status: SecurityConstants.Statuses.Revoked));
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Form_Client_Id_Differing_From_The_Assertion() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion, "client-2"), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Assertion_Without_A_Client_Identity() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256, subject: null);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => subject.AuthenticateAsync(null, Form(assertion), null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Return_Null_When_The_Method_Is_Not_Allowed() {
        var app     = CreateApp();
        var subject = CreateSubject(app, new() { Issuer = Issuer });

        var assertion = Mint(Key(Secret), SigningAlgorithms.HmacSha256);
        var result    = await subject.AuthenticateAsync(null, Form(assertion), null, default);

        Assert.Null(result);
    }
    [Fact]
    public async Task Return_Null_When_No_Assertion_Is_Presented() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        Assert.Null(await subject.AuthenticateAsync(null, null, null, default));
        Assert.Null(await subject.AuthenticateAsync(null, new(), null, default));
        Assert.Null(await subject.AuthenticateAsync(
            null,
            new() { [Parameters.ClientId] = [ClientId] },
            null,
            default));
    }
    [Fact]
    public async Task Return_Null_When_The_Assertion_Type_Is_Not_Jwt_Bearer() {
        var app     = CreateApp();
        _rows.Add(SecretRow());
        var subject = CreateSubject(app, AllowedOptions());

        var form = new Dictionary<string, List<string?>> {
            [Parameters.ClientAssertionType] = ["urn:ietf:params:oauth:client-assertion-type:saml2-bearer"],
            [Parameters.ClientAssertion]     = [Mint(Key(Secret), SigningAlgorithms.HmacSha256)],
        };

        Assert.Null(await subject.AuthenticateAsync(null, form, null, default));
    }

    private static SchemataApplication CreateApp() {
        return new() {
            Uid        = Guid.NewGuid(),
            ClientId   = ClientId,
            ClientType = ClientTypes.Confidential,
        };
    }

    private SchemataSecurity SecretRow(string? kid = null, string? value = null, string? status = null) {
        return new() {
            Uid    = Guid.NewGuid(),
            Parent = SecurityParents.Application(new() { ClientId = ClientId }),
            Name   = ClientId,
            Kind   = SecurityConstants.Kinds.Secret,
            Usage  = SecurityConstants.Usages.Authentication,
            Kid    = kid,
            Value  = value ?? Secret,
            Status = status ?? SecurityConstants.Statuses.Valid,
        };
    }

    private ClientSecretJwtAuthentication<SchemataApplication> CreateSubject(
        SchemataApplication          app,
        SchemataAuthorizationOptions options,
        ClientAssertionValidator?    assertions = null
    ) {
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

        return new(
            manager.Object,
            Options.Create(options),
            new StubHttpClientFactory(),
            new Mock<ICacheProvider>().Object,
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
        options.AllowedClientAuthMethods.Add(ClientAuthMethods.ClientSecretJwt);
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

    private static Mock<ITokenStore<SchemataToken>> ReplaySlots() {
        var burned = false;
        var slots  = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(
                 value => value.GetOrCreateAsync(
                     It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                     It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan _, CancellationToken _) => {
                if (burned) {
                    return new() { Provider = "assertion", Value = "pre-existing" };
                }

                burned = true;
                return new() { Parent = parent, Provider = provider, Name = name, Value = marker };
            });

        return slots;
    }

    private static Dictionary<string, List<string?>> Form(string assertion, string? clientId = null) {
        var form = new Dictionary<string, List<string?>> {
            [Parameters.ClientAssertionType] = [ClientAssertionTypes.JwtBearer],
            [Parameters.ClientAssertion]     = [assertion],
        };

        if (clientId is not null) {
            form[Parameters.ClientId] = [clientId];
        }

        return form;
    }

    private static SymmetricSecurityKey Key(string secret) {
        return new(Encoding.UTF8.GetBytes(secret));
    }

    private static string Mint(
        SymmetricSecurityKey   key,
        string                 algorithm,
        string?                subject = ClientId,
        string?                jti     = null
    ) {
        var claims = new Dictionary<string, object> {
            ["aud"] = new[] { Issuer + Endpoints.Token },
            ["jti"] = jti ?? Guid.NewGuid().ToString(),
        };
        if (subject is not null) {
            claims["sub"] = subject;
        }

        var descriptor = new SecurityTokenDescriptor {
            Issuer             = ClientId,
            Claims             = claims,
            Expires            = Now.AddMinutes(5).UtcDateTime,
            NotBefore          = Now.AddMinutes(-1).UtcDateTime,
            IssuedAt           = Now.AddMinutes(-1).UtcDateTime,
            SigningCredentials = new(key, algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { return new(); }
    }
}
