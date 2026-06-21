using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Push.Foundation;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests;

public class DefaultPushSubscriptionManagerShould
{
    [Fact]
    public async Task Add_PersistsRow_AndCommits() {
        var repo    = new FakePushRepository();
        var manager = new DefaultPushSubscriptionManager(repo);

        var subscription = await manager.AddAsync("users/chino", "fcm", "device-token-1", [], CancellationToken.None);

        Assert.Single(repo.Store);
        Assert.Equal(1, repo.Commits);
        Assert.Equal("users/chino", subscription.Owner);
        Assert.Equal("fcm", subscription.Provider);
        Assert.Equal("device-token-1", subscription.ProviderKey);
        Assert.Equal([], subscription.Metadata);
        Assert.NotEqual(Guid.Empty, subscription.Uid);
    }

    [Fact]
    public async Task Add_WhenTripleAlreadyExists_ReturnsExisting_WithoutDuplicate() {
        var existing = Row("users/chino", "fcm", "device-token-1");
        var repo     = new FakePushRepository();
        repo.Seed(existing);
        var manager = new DefaultPushSubscriptionManager(repo);

        var result = await manager.AddAsync("users/chino", "fcm", "device-token-1", null, CancellationToken.None);

        Assert.Single(repo.Store);
        Assert.Equal(0, repo.Commits);
        Assert.Same(existing, result);
    }

    [Fact]
    public async Task Add_DistinctProviderKeys_ForSameOwnerProvider_AreSeparateRows() {
        var repo    = new FakePushRepository();
        var manager = new DefaultPushSubscriptionManager(repo);

        await manager.AddAsync("users/chino", "fcm", "device-a", null, CancellationToken.None);
        await manager.AddAsync("users/chino", "fcm", "device-b", null, CancellationToken.None);

        Assert.Equal(2, repo.Store.Count);
    }

    [Fact]
    public async Task GetForOwner_ReturnsOnlyMatchingOwner() {
        var repo = new FakePushRepository();
        repo.Seed(
            Row("users/chino", "fcm", "a"),
            Row("users/chino", "apns", "b"),
            Row("groups/admins", "fcm", "c"));
        var manager = new DefaultPushSubscriptionManager(repo);

        var rows = await CollectAsync(manager.GetForOwnerAsync("users/chino", null, CancellationToken.None));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("users/chino", r.Owner));
    }

    [Fact]
    public async Task GetForOwner_WithProviderFilter_NarrowsToTransport() {
        var repo = new FakePushRepository();
        repo.Seed(
            Row("users/chino", "fcm", "a"),
            Row("users/chino", "apns", "b"));
        var manager = new DefaultPushSubscriptionManager(repo);

        var rows = await CollectAsync(manager.GetForOwnerAsync("users/chino", "apns", CancellationToken.None));

        Assert.Single(rows);
        Assert.Equal("apns", rows[0].Provider);
    }

    [Fact]
    public async Task Exists_TrueWhenTriplePresent_FalseOtherwise() {
        var repo = new FakePushRepository();
        repo.Seed(Row("users/chino", "fcm", "device-1"));
        var manager = new DefaultPushSubscriptionManager(repo);

        Assert.True(await manager.ExistsAsync("users/chino", "fcm", "device-1", CancellationToken.None));
        Assert.False(await manager.ExistsAsync("users/chino", "fcm", "missing", CancellationToken.None));
        Assert.False(await manager.ExistsAsync("users/other", "fcm", "device-1", CancellationToken.None));
    }

    [Fact]
    public async Task Remove_DeletesMatchingRow_AndCommits() {
        var row  = Row("users/chino", "fcm", "device-1");
        var repo = new FakePushRepository();
        repo.Seed(row);
        var manager = new DefaultPushSubscriptionManager(repo);

        await manager.RemoveAsync("users/chino", "fcm", "device-1", CancellationToken.None);

        Assert.Empty(repo.Store);
        Assert.Equal(1, repo.Commits);
    }

    [Fact]
    public async Task Remove_WhenAbsent_IsNoOp() {
        var repo    = new FakePushRepository();
        var manager = new DefaultPushSubscriptionManager(repo);

        await manager.RemoveAsync("users/chino", "fcm", "missing", CancellationToken.None);

        Assert.Equal(0, repo.Commits);
    }

    private static SchemataPushSubscription Row(string owner, string provider, string providerKey) {
        return new() {
            Uid         = Guid.NewGuid(),
            Owner       = owner,
            Provider    = provider,
            ProviderKey = providerKey,
        };
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source) {
        var list = new List<T>();
        await foreach (var item in source) {
            list.Add(item);
        }

        return list;
    }
}
