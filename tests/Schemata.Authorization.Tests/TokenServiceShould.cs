using System;
using System.Net.Http;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class TokenServiceShould
{
    private const string Issuer = "https://as.example";

    private static TokenService CreateService(bool withEncryption) {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        return TestSecurityKeys.CreateTokenService(options, encryption: withEncryption);
    }

    [Fact]
    public async Task Discover_All_Usable_Active_Signing_Algorithms_Without_Retired_Keys() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, Issuer);
        TestSecurityKeys.AddSigningRow(store, Issuer, SecurityConstants.Algorithms.P256);
        TestSecurityKeys.AddSigningRow(store, Issuer, SecurityConstants.Algorithms.P384).Status = SecurityConstants.Statuses.Retired;
        var service = TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer }, store, seed: false);
        Assert.Equal(new[] { SigningAlgorithms.EcdsaSha256, SigningAlgorithms.RsaSha256 },
            (await service.GetSigningAlgorithmsAsync(default)).OrderBy(value => value));
    }

    [Fact]
    public async Task Reject_Discovery_Of_An_Active_Key_Without_Signing_Material() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, Issuer).Value = null;
        var service = TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer }, store, seed: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSigningAlgorithmsAsync(default));
    }

    [Theory]
    [InlineData(null, -60, -600, true)]
    [InlineData(null, -61, -600, false)]
    [InlineData(0, 0, -600, true)]
    [InlineData(0, -1, -600, false)]
    [InlineData(120, -120, -600, true)]
    [InlineData(120, -121, -600, false)]
    [InlineData(0, 600, 0, true)]
    [InlineData(0, 600, 1, false)]
    [InlineData(120, 600, 120, true)]
    [InlineData(120, 600, 121, false)]
    public async Task Validate_Lifetime_Using_Injected_Clock_And_Configured_Skew(int? skew, int expiry, int notBefore, bool valid) {
        var now = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        if (skew is { } seconds) options.TokenValidationClockSkew = TimeSpan.FromSeconds(seconds);
        var service = TestSecurityKeys.CreateTokenService(options, time: new FakeTimeProvider(now));
        await using var signing = await service.BeginSigningAsync();
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor {
            Issuer = Issuer,
            Subject = new ClaimsIdentity([new Claim("sub", "users/clock")]),
            SigningCredentials = signing.Signing,
            Expires = now.AddSeconds(expiry).UtcDateTime,
            NotBefore = now.AddSeconds(notBefore).UtcDateTime,
        });
        var principal = await service.Validate(token);
        if (valid) Assert.Equal("users/clock", principal?.FindFirst("sub")?.Value);
        else Assert.Null(principal);
        Assert.Equal("users/clock", (await service.Validate(token, lifetime: false))?.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Stamp_The_Access_Token_Type_Header() {
        var jwt = await CreateService(false).CreateToken([], TimeSpan.FromHours(1), typ: "at+jwt");

        var token = new JsonWebTokenHandler().ReadJsonWebToken(jwt);
        Assert.Equal("at+jwt", token.Typ);
    }
    [Fact]
    public async Task Stamp_The_Inner_Type_And_Outer_Cty_For_Nested_Access_Tokens() {
        var jwe = await CreateService(true).CreateToken([], TimeSpan.FromHours(1), encrypt: true, typ: "at+jwt");

        var outer = new JsonWebTokenHandler().ReadJsonWebToken(jwe);
        Assert.Equal("JWT", outer.Cty);
    }
    [Fact]
    public async Task Stamp_The_Logout_Token_Type_Header() {
        var jwt = await CreateService(false).CreateToken(
            [new("sub", "user-1")], TimeSpan.FromMinutes(2), typ: "logout+jwt");

        Assert.Equal("logout+jwt", new JsonWebTokenHandler().ReadJsonWebToken(jwt).Typ);
    }

    [Fact]
    public async Task Rotate_To_The_Newest_Valid_Row_And_Validate_Tokens_Signed_By_Any_Row() {
        var store = new TestSecurityStore();
        var older = TestSecurityKeys.AddSigningRow(store, Issuer);
        var newer = TestSecurityKeys.AddSigningRow(store, Issuer);
        older.CreateTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        newer.CreateTime = DateTime.UtcNow;
        var service = CreateTokenService(store);

        var jwt       = await service.CreateToken([], TimeSpan.FromMinutes(5));
        var token     = new JsonWebTokenHandler().ReadJsonWebToken(jwt);
        var principal = await service.Validate(jwt);

        Assert.Equal(newer.Kid, token.Kid);
        Assert.NotNull(principal);

        // The rotation half of the contract: a token signed directly by the older
        // (still valid) row's key must validate through the same service.
        Assert.NotNull(await service.Validate(SignWithRow(older)));
    }

    [Fact]
    public async Task Still_Validate_Tokens_Signed_By_Retired_Rows() {
        var store = new TestSecurityStore();
        var retired = TestSecurityKeys.AddSigningRow(store, Issuer);
        retired.Status     = SecurityConstants.Statuses.Retired;
        retired.CreateTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        var valid = TestSecurityKeys.AddSigningRow(store, Issuer);
        var service = CreateTokenService(store);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(await service.CreateToken([], TimeSpan.FromMinutes(5)));

        Assert.Equal(valid.Kid, token.Kid);
        Assert.NotNull(await service.Validate(SignWithRow(retired)));
    }

    [Fact]
    public async Task Reject_A_Multi_Row_Set_Where_Any_Row_Lacks_A_Key_Id() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, Issuer);
        var blank = TestSecurityKeys.AddSigningRow(store, Issuer);
        blank.Kid = null;
        var service = CreateTokenService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateToken([], TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Return_Claims_That_Survive_Validation_Key_Disposal() {
        var service = CreateService(false);
        var jwt     = await service.CreateToken(
            [new("sub", "user-1"), new("Sub", "user-2")], TimeSpan.FromMinutes(5));

        var principal = await service.Validate(jwt);

        // Validation disposes the resolved keys before returning; the detached identity
        // keeps every claim with its exact case-sensitive type.
        Assert.NotNull(principal);
        Assert.Equal("user-1", principal.FindFirst("sub")?.Value);
        Assert.Equal("user-2", principal.FindFirst("Sub")?.Value);
    }

    [Fact]
    public async Task Return_Detached_Metadata_For_Nested_Tokens_After_Key_Disposal() {
        var service = CreateService(true);
        var jwe     = await service.CreateToken(
            [new("sub", "user-1")], TimeSpan.FromHours(1), encrypt: true, typ: "at+jwt");

        // The inner typ is read from detached metadata; the verified token that retained
        // the disposed keys is no longer handed out.
        var principal = await service.ValidateAccessToken(jwe);

        Assert.NotNull(principal);
        Assert.Equal("user-1", principal.FindFirst("sub")?.Value);
        var (_, expires) = await service.ValidateForLogout(jwe);
        Assert.NotNull(expires);
    }

    [Fact]
    public async Task Reject_Issuance_When_No_Valid_Signing_Row_Exists() {
        var service = CreateTokenService(new());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateToken([], TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Decrypt_Retired_Key_Tokens_While_Issuing_With_Current_Key() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, Issuer);
        var old = TestSecurityKeys.AddEncryptionRow(store, Issuer);
        var service = CreateTokenService(store);
        var original = await service.CreateToken([new("sub", "user-1")], TimeSpan.FromMinutes(5), encrypt: true);
        old.Status = SecurityConstants.Statuses.Retired;
        var current = TestSecurityKeys.AddEncryptionRow(store, Issuer);
        var successor = await service.CreateToken([new("sub", "user-1")], TimeSpan.FromMinutes(5), encrypt: true);

        Assert.Equal("user-1", (await service.Validate(original))?.FindFirst("sub")?.Value);
        Assert.Equal(current.Kid, new JsonWebToken(successor).Kid);
        old.Status = SecurityConstants.Statuses.Revoked;
        Assert.Null(await service.Validate(original));
        Assert.Equal("user-1", (await service.Validate(successor))?.FindFirst("sub")?.Value);
    }

    private static TokenService CreateTokenService(TestSecurityStore store) {
        return new(
            store,
            new StubHttpClientFactory(),
            new Mock<ICacheProvider>().Object,
            Options.Create(new SchemataSecurityOptions()),
            Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer }));
    }

    private static string SignWithRow(SchemataSecurity row) {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(row.Value);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Issuer             = Issuer,
            SigningCredentials = new(new RsaSecurityKey(rsa), SigningAlgorithms.RsaSha256),
        });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { return new(); }
    }
}
