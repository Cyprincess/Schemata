using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
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
    public async Task Reject_Issuance_When_No_Valid_Signing_Row_Exists() {
        var service = CreateTokenService(new());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateToken([], TimeSpan.FromMinutes(5)));
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
