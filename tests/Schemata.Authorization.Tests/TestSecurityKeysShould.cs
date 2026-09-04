using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Proves the fixture rows round-trip the real <see cref="SecurityStore{TSecurity}" />:
///     created through <c>CreateAsync</c>, listed back under the issuer parent with the
///     store's usage and status filters, and publishable by the JWKS endpoint.
/// </summary>
public class TestSecurityKeysShould
{
    private const string Issuer = "https://as.example";

    [Fact]
    public async Task Seed_Rows_Through_A_Real_SecurityStore_And_List_Them_Back() {
        var store = NewStore();

        var signing    = TestSecurityKeys.AddSigningRow(store, Issuer);
        var encryption = TestSecurityKeys.AddEncryptionRow(store, Issuer);

        var signingRows = await CollectAsync(store.ListByParentAsync(
            SecurityParents.Issuer(Issuer), null, SecurityConstants.Usages.Signing, null));
        var encryptionRows = await CollectAsync(store.ListByParentAsync(
            SecurityParents.Issuer(Issuer), null, SecurityConstants.Usages.Encryption, null));
        var validSigningRows = await CollectAsync(store.ListByParentAsync(
            SecurityParents.Issuer(Issuer), null, SecurityConstants.Usages.Signing, SecurityConstants.Statuses.Valid));

        Assert.Equal([signing.Kid], signingRows.Select(row => row.Kid));
        Assert.Equal([encryption.Kid], encryptionRows.Select(row => row.Kid));
        Assert.Equal([signing.Kid], validSigningRows.Select(row => row.Kid));
        Assert.Equal(SecurityConstants.Kinds.PrivateKey, signingRows.Single().Kind);
    }

    [Fact]
    public async Task Publish_The_Seeded_Row_Through_A_JwksHandler_Over_A_Real_SecurityStore() {
        var store   = NewStore();
        var options = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });
        var row     = TestSecurityKeys.AddSigningRow(store, Issuer);

        var result = await new JwksHandler(
            store,
            new StubHttpClientFactory(),
            new Mock<ICacheProvider>().Object,
            Options.Create(new SchemataSecurityOptions()),
            options).ExecuteAsync();
        var json   = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        var keys   = json.RootElement.GetProperty("keys");

        Assert.Equal(1, keys.GetArrayLength());
        Assert.Equal(row.Kid, keys[0].GetProperty("kid").GetString());
        Assert.Equal(SigningAlgorithms.RsaSha256, keys[0].GetProperty("alg").GetString());
    }

    private static SecurityStore<SchemataSecurity> NewStore() {
        var rows       = new List<SchemataSecurity>();
        var repository = new Mock<IRepository<SchemataSecurity>>();
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataSecurity>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataSecurity, CancellationToken>((row, _) => rows.Add(row))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataSecurity>, IQueryable<SchemataSecurity>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns<Func<IQueryable<SchemataSecurity>, IQueryable<SchemataSecurity>>, CancellationToken>(
                      (query, ct) => Enumerate(query(rows.AsQueryable()), ct));

        return new(repository.Object);
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(
        IEnumerable<SchemataSecurity> rows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
    ) {
        foreach (var row in rows) {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    private static async Task<List<SchemataSecurity>> CollectAsync(IAsyncEnumerable<SchemataSecurity> rows) {
        var list = new List<SchemataSecurity>();
        await foreach (var row in rows) {
            list.Add(row);
        }

        return list;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { return new(); }
    }
}
