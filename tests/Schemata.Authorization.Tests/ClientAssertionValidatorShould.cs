using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ClientAssertionValidatorShould
{
    private const string Issuer   = "https://as.example";
    private const string Audience = "https://as.example/token";
    private const string ClientId = "client-1";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private static readonly ISet<string> AllowedAlgorithms =
        new HashSet<string>(StringComparer.Ordinal) { "RS256", "HS256" };
    private static ClientAssertionValidator Validator(Mock<ITokenStore<SchemataToken>>? slots = null) {
        var mock = slots ?? new Mock<ITokenStore<SchemataToken>>();
        if (slots is null) {
            Setup_Burn_Wins(mock);
        }

        return new(mock.Object, Options.Create(new SchemataAuthorizationOptions()), new FakeTimeProvider(Now));
    }

    private static Task<JsonWebToken> Validate(
        ClientAssertionValidator validator,
        string                    assertion,
        IReadOnlyList<string>?    audiences = null,
        ISet<string>?             allowed   = null
    ) {
        return validator.ValidateAsync(assertion, ClientId, Issuer, audiences ?? [Audience], allowed ?? AllowedAlgorithms, default);
    }

    private static string Mint(
        string?                issuer    = Issuer,
        string?                subject   = ClientId,
        IReadOnlyList<string>? audiences = null,
        DateTimeOffset?        expires   = null,
        DateTimeOffset?        notBefore = null,
        DateTimeOffset?        issuedAt  = null,
        string?                jti       = "unique",
        string?                algorithm = "RS256"
    ) {
        var claims = new Dictionary<string, object>();
        if (subject is not null) {
            claims["sub"] = subject;
        }

        claims["aud"] = audiences ?? [Audience];
        if (jti is not null) {
            claims["jti"] = jti;
        }

        var descriptor = new SecurityTokenDescriptor {
            Issuer   = issuer,
            Claims   = claims,
            Expires  = (expires ?? Now.AddMinutes(5)).UtcDateTime,
            NotBefore = (notBefore ?? Now.AddMinutes(-1)).UtcDateTime,
            IssuedAt = (issuedAt ?? Now.AddMinutes(-1)).UtcDateTime,
            SigningCredentials = algorithm is null
                ? null
                : new SigningCredentials(CreateKey(algorithm), algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static SecurityKey CreateKey(string algorithm) {
        return algorithm.StartsWith("HS", StringComparison.Ordinal)
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))
            : new RsaSecurityKey(RSA.Create(2048));
    }

    // JsonWebTokenHandler.CreateToken always fills exp/iat/nbf, so claims whose absence
    // matters are minted by assembling the three segments directly.
    private static string Raw(object header, object payload) {
        return Base64UrlEncoder.Encode(JsonSerializer.Serialize(header)) + "."
             + Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload)) + "."
             + Base64UrlEncoder.Encode("unused-signature");
    }

    private static void Setup_Burn_Wins(Mock<ITokenStore<SchemataToken>> slots) {
        slots.Setup(
                value => value.GetOrCreateAsync(
                    It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan _, CancellationToken _) =>
                new() { Parent = parent, Provider = provider, Key = name, Value = marker });
    }

    [Theory]
    [InlineData("exp", long.MaxValue)]
    [InlineData("exp", long.MinValue)]
    [InlineData("nbf", long.MaxValue)]
    [InlineData("nbf", long.MinValue)]
    [InlineData("iat", long.MaxValue)]
    [InlineData("iat", long.MinValue)]
    public async Task Reject_An_Out_Of_Range_NumericDate_As_A_Protocol_Error(string claim, long seconds) {
        var slots = new Mock<ITokenStore<SchemataToken>>();
        var payload = new Dictionary<string, object> {
            ["iss"] = Issuer,
            ["sub"] = ClientId,
            ["aud"] = Audience,
            ["jti"] = "unique",
            ["exp"] = Now.AddMinutes(5).ToUnixTimeSeconds(),
            [claim] = seconds,
        };
        var assertion = Raw(new { alg = "RS256", typ = "JWT" }, payload);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(slots), assertion));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        slots.Verify(
            value => value.GetOrCreateAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Accept_A_Valid_Assertion_And_Return_The_Parsed_Token() {
        var token = await Validate(Validator(), Mint(audiences: [Audience, "https://other.example"]));

        Assert.Equal("unique", token.Id);
        Assert.Equal(Issuer, token.Issuer);
        Assert.Equal(ClientId, token.Subject);
        Assert.Equal("RS256", token.Alg);
    }
    [Fact]
    public async Task Accept_An_Hs256_Assertion_Allowed_By_The_Callers_List() {
        var token = await Validate(
            Validator(),
            Mint(algorithm: "HS256"),
            allowed: new HashSet<string>(StringComparer.Ordinal) { "HS256" });

        Assert.Equal("HS256", token.Alg);
    }
    [Fact]
    public async Task Accept_An_Assertion_Expiring_Within_The_Clock_Skew_Tolerance() {
        var token = await Validate(Validator(), Mint(expires: Now.AddSeconds(-30)));

        Assert.Equal("unique", token.Id);
    }

    [Fact]
    public async Task Reject_A_Wrong_Issuer() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(issuer: "https://evil.example")));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Subject_Other_Than_The_Client_Identifier() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(subject: "client-2")));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Missing_Subject() {
        var assertion = Raw(
            new { alg = "RS256", typ = "JWT" },
            new { iss = Issuer, aud = new[] { Audience }, jti = "unique" });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(), assertion));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Audience_Missing_An_Expected_Value() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(audiences: ["https://other.example"])));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Missing_Audience() {
        var assertion = Raw(
            new { alg = "RS256", typ = "JWT" },
            new { iss = Issuer, sub = ClientId, jti = "unique" });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(), assertion));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Expired_Assertion() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(expires: Now.AddMinutes(-2), notBefore: Now.AddMinutes(-3), issuedAt: Now.AddMinutes(-3))));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Missing_Expiration() {
        var assertion = Raw(
            new { alg = "RS256", typ = "JWT" },
            new { iss = Issuer, sub = ClientId, aud = new[] { Audience }, jti = "unique" });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(), assertion));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Not_Yet_Valid_Assertion() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(notBefore: Now.AddMinutes(5), expires: Now.AddMinutes(10))));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Future_Issued_At() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(issuedAt: Now.AddMinutes(5), expires: Now.AddMinutes(10))));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }

    [Fact]
    public async Task Reject_A_Missing_Jti() {
        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(), Mint(jti: null)));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Empty_Jti() {
        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(), Mint(jti: "")));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Replayed_Jti() {
        var slots = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(
                 value => value.GetOrCreateAsync(
                     It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                     It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new SchemataToken { Provider = "assertion", Value = "pre-existing" });
        var validator = Validator(slots);

        var token = await Validate(validator, Mint());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => validator.BurnJtiAsync(token));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public async Task Leave_The_Replay_Slots_Untouched_During_Validation() {
        var slots = new Mock<ITokenStore<SchemataToken>>();

        await Validate(Validator(slots), Mint());

        slots.Verify(
            value => value.GetOrCreateAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reject_The_None_Algorithm() {
        var slots = new Mock<ITokenStore<SchemataToken>>();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(slots), Mint(algorithm: null)));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        slots.Verify(
            value => value.GetOrCreateAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    [Fact]
    public async Task Reject_An_Algorithm_Outside_The_Callers_List() {
        var slots = new Mock<ITokenStore<SchemataToken>>();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(slots), Mint(), allowed: new HashSet<string>(StringComparer.Ordinal) { "ES256" }));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        slots.Verify(
            value => value.GetOrCreateAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    [InlineData("!!!.!!!.!!!")]
    public async Task Reject_A_Malformed_Assertion(string assertion) {
        var slots = new Mock<ITokenStore<SchemataToken>>();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(slots), assertion));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        slots.Verify(
            value => value.GetOrCreateAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0, "exp", 0, false)]
    [InlineData(0, "exp", 1, true)]
    [InlineData(120, "exp", -120, false)]
    [InlineData(120, "exp", -119, true)]
    [InlineData(0, "nbf", 0, true)]
    [InlineData(0, "nbf", 1, false)]
    [InlineData(120, "nbf", 120, true)]
    [InlineData(120, "nbf", 121, false)]
    [InlineData(120, "iat", 120, true)]
    [InlineData(120, "iat", 121, false)]
    public async Task Apply_Configured_Tolerance_At_Timestamp_Boundaries(int skew, string claim, int offset, bool valid) {
        var options = new SchemataAuthorizationOptions { TokenValidationClockSkew = TimeSpan.FromSeconds(skew) };
        var validator = new ClientAssertionValidator(Mock.Of<ITokenStore<SchemataToken>>(), Options.Create(options), new FakeTimeProvider(Now));
        var claims = new Dictionary<string, object> {
            ["iss"] = Issuer, ["sub"] = ClientId, ["aud"] = Audience, ["jti"] = "boundary",
            ["exp"] = Now.AddHours(1).ToUnixTimeSeconds(),
            [claim] = Now.AddSeconds(offset).ToUnixTimeSeconds(),
        };
        var token = Raw(new { alg = "RS256" }, claims);
        if (valid) {
            Assert.Equal("boundary", (await Validate(validator, token)).Id);
        } else {
            var error = await Assert.ThrowsAsync<OAuthException>(() => Validate(validator, token));
            Assert.Equal(OAuthErrors.InvalidClient, error.Status);
        }
    }

    [Fact]
    public async Task Reject_Replay_Throughout_The_Configured_Tolerance_Window() {
        var time = new FakeTimeProvider(Now);
        var slots = new Mock<ITokenStore<SchemataToken>>();
        SchemataToken? stored = null;
        var deadline = Now;
        slots.Setup(value => value.GetOrCreateAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan lifetime, CancellationToken _) => {
                if (stored is null || time.GetUtcNow() >= deadline) {
                    stored = new() { Parent = parent, Provider = provider, Key = name, Value = marker };
                    deadline = time.GetUtcNow() + lifetime;
                }
                return stored;
            });
        var options = new SchemataAuthorizationOptions { TokenValidationClockSkew = TimeSpan.FromMinutes(10) };
        var validator = new ClientAssertionValidator(slots.Object, Options.Create(options), time);
        var assertion = Mint(expires: Now.AddMinutes(1));
        await validator.BurnJtiAsync(await Validate(validator, assertion));
        time.Advance(TimeSpan.FromMinutes(6));
        var stillValid = await Validate(validator, assertion);
        var error = await Assert.ThrowsAsync<OAuthException>(() => validator.BurnJtiAsync(stillValid));
        Assert.Equal(OAuthErrors.InvalidClient, error.Status);
    }
}
