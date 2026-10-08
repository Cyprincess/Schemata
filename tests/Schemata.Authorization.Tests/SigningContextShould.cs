using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Behavioral coverage for the operation-local signing selection: the negotiated algorithm
///     governs the JOSE signature and every hash claim of the issued token, one store resolution
///     serves the whole operation, and imported key material dies with the operation.
/// </summary>
public class SigningContextShould
{
    private const string Issuer = "https://as.example";

    [Fact]
    public async Task Sign_And_Hash_The_Id_Token_With_The_Negotiated_Algorithm() {
        var store = new TestSecurityStore();
        var ec    = SeedEc384(store);
        SeedPrimary(store);
        var service  = CreateService(store);
        var issuedAt = DateTimeOffset.UtcNow;

        await using var context = await service.BeginSigningAsync(SigningAlgorithms.EcdsaSha384);
        var id = service.CreateIdToken(
            context,
            [new(IdentityClaims.Subject, "user-1")],
            issuedAt, issuedAt + TimeSpan.FromHours(1),
            at: "access-token-1", code: "code-1");

        var token = new JsonWebTokenHandler().ReadJsonWebToken(id);
        Assert.Equal(SigningAlgorithms.EcdsaSha384, token.Alg);
        Assert.Equal(ec.Kid, token.Kid);
        Assert.Equal(ComputeSha384Hash("access-token-1"), token.GetClaim(Claims.AtHash).Value);
        Assert.Equal(ComputeSha384Hash("code-1"), token.GetClaim(Claims.CHash).Value);
        Assert.NotNull(await service.Validate(id));
    }

    [Fact]
    public async Task Fail_When_The_Negotiated_Algorithm_Has_No_Serving_Row() {
        var store = new TestSecurityStore();
        SeedPrimary(store);
        var service = CreateService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginSigningAsync(SigningAlgorithms.EcdsaSha384));
    }

    [Fact]
    public async Task Fall_Back_To_The_Primary_Selection_Without_A_Negotiated_Algorithm() {
        var store = new TestSecurityStore();
        SeedEc384(store);
        var rsa     = SeedPrimary(store);
        var service = CreateService(store);

        await using var context = await service.BeginSigningAsync();

        Assert.Same(context.Primary, context.Signing);
        Assert.Equal(SigningAlgorithms.RsaSha256, context.Algorithm);
        Assert.Equal(rsa.Kid, context.Signing.Key.KeyId);
    }

    [Fact]
    public async Task Resolve_The_Key_Store_Once_For_The_Whole_Operation() {
        var store = new TestSecurityStore();
        SeedPrimary(store);
        var service  = CreateService(store);
        var issuedAt = DateTimeOffset.UtcNow;

        await using var context = await service.BeginSigningAsync();
        var           queries = store.ListByParentCalls;
        var           at      = service.CreateToken(context, context.Signing, [], issuedAt, issuedAt + TimeSpan.FromHours(1));
        service.CreateToken(context, context.Signing, [], issuedAt, issuedAt + TimeSpan.FromHours(1));
        var id = service.CreateIdToken(
            context,
            [new(IdentityClaims.Subject, "user-1")],
            issuedAt, issuedAt + TimeSpan.FromHours(1),
            at: at, code: "code-1");

        Assert.Equal(queries, store.ListByParentCalls);
        var idToken = new JsonWebTokenHandler().ReadJsonWebToken(id);
        Assert.Equal(TokenService.ComputeHash(at, context.Algorithm), idToken.GetClaim(Claims.AtHash).Value);
    }

    [Fact]
    public async Task Dispose_The_Owned_Key_Material_When_The_Operation_Ends() {
        var store = new TestSecurityStore();
        SeedPrimary(store);
        var service  = CreateService(store);
        var issuedAt = DateTimeOffset.UtcNow;
        var context  = await service.BeginSigningAsync();
        var jwt      = service.CreateToken(context, context.Signing, [], issuedAt, issuedAt + TimeSpan.FromHours(1));
        Assert.False(string.IsNullOrWhiteSpace(jwt));

        var key = Assert.IsType<RsaSecurityKey>(context.Signing.Key);
        var rsa = Assert.IsAssignableFrom<RSA>(key.Rsa);
        await context.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => rsa.SignData([1, 2, 3], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
    private static string ComputeSha384Hash(string value) {
        var digest = SHA384.HashData(Encoding.ASCII.GetBytes(value));
        return Base64UrlEncoder.Encode(digest.AsSpan(0, digest.Length / 2).ToArray());
    }


    private static TokenService CreateService(TestSecurityStore store) {
        return TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer }, store, seed: false);
    }

    private static SchemataSecurity SeedPrimary(TestSecurityStore store) {
        var row = TestSecurityKeys.AddSigningRow(store, Issuer);
        row.CreateTime = DateTime.UtcNow;
        return row;
    }

    private static SchemataSecurity SeedEc384(TestSecurityStore store) {
        var row = TestSecurityKeys.AddSigningRow(store, Issuer, SecurityConstants.Algorithms.P384);
        row.CreateTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        return row;
    }
}
