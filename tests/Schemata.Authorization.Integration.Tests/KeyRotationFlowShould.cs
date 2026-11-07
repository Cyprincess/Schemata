using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class KeyRotationFlowShould
{
    private const string Issuer = "https://localhost";

    [Fact]
    public async Task Rotate_Signing_And_Encryption_Preserves_Previous_Tokens_And_Publishes_Both_Signing_Generations() {
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService<SchemataSecurity>>();
        var rotation = scope.ServiceProvider.GetRequiredService<SecurityKeyRotationService<SchemataSecurity>>();
        var parent = SecurityParents.Issuer(Issuer);
        var previousSigning = await ActiveAsync(store, parent, SecurityConstants.Usages.Signing);
        var previousEncryption = await ActiveAsync(store, parent, SecurityConstants.Usages.Encryption);
        var previousJwt = await IssueAsync(tokens, encrypt: false);
        var previousJwe = await IssueAsync(tokens, encrypt: true);

        var currentSigning = await rotation.RotateAsync(
            parent, SecurityConstants.Usages.Signing, SecurityConstants.Algorithms.Rsa, default);
        var currentEncryption = await rotation.RotateAsync(
            parent, SecurityConstants.Usages.Encryption, SecurityConstants.Algorithms.Rsa, default);
        var currentJwt = await IssueAsync(tokens, encrypt: false);
        var currentJwe = await IssueAsync(tokens, encrypt: true);

        Assert.NotEqual(previousSigning.Kid, currentSigning.Kid);
        Assert.NotEqual(previousEncryption.Kid, currentEncryption.Kid);
        Assert.Equal("users/key-rotation", (await tokens.Validate(previousJwt))?.FindFirst("sub")?.Value);
        Assert.Equal("users/key-rotation", (await tokens.Validate(previousJwe))?.FindFirst("sub")?.Value);
        Assert.Equal("users/key-rotation", (await tokens.Validate(currentJwt))?.FindFirst("sub")?.Value);
        Assert.Equal("users/key-rotation", (await tokens.Validate(currentJwe))?.FindFirst("sub")?.Value);

        using var jwks = JsonDocument.Parse(await client.GetStringAsync("/.well-known/jwks"));
        var kids = jwks.RootElement.GetProperty("keys").EnumerateArray()
            .Select(key => key.GetProperty("kid").GetString()).ToList();
        Assert.Contains(previousSigning.Kid, kids);
        Assert.Contains(currentSigning.Kid, kids);
        Assert.DoesNotContain(previousEncryption.Kid, kids);
        Assert.DoesNotContain(currentEncryption.Kid, kids);
    }

    private static async Task<string> IssueAsync(TokenService<SchemataSecurity> tokens, bool encrypt) {
        var now = DateTimeOffset.UtcNow;
        await using var signing = await tokens.BeginSigningAsync();
        return tokens.CreateToken(
            signing,
            signing.Signing,
            [new Claim("sub", "users/key-rotation")],
            now,
            now.AddMinutes(5),
            encrypt);
    }

    private static async Task<SchemataSecurity> ActiveAsync(
        ISecurityStore<SchemataSecurity> store,
        string parent,
        string usage
    ) {
        await foreach (var row in store.ListByParentAsync(parent, usage: usage, status: SecurityConstants.Statuses.Valid)) {
            return row;
        }

        throw new InvalidOperationException($"The initial {usage} key is missing.");
    }
}
