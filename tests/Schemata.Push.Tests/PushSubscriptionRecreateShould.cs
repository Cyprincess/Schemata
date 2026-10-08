using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Tests.Fixtures;
using Xunit;

namespace Schemata.Push.Tests;

[Trait("Category", "Integration")]
public abstract class PushSubscriptionRecreateShould
{
    protected abstract IPushSubscriptionFixture CreateFixture();

    [Fact]
    public async Task Recreate_ForARetainedTriple_UndeletesTheExistingRow() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            var created = await fixture.AddAsync(
                new("users/1", "fcm", "token-1", new Dictionary<string, string?> { ["endpoint"] = "alpha" }));
            await fixture.RemoveAsync(new("users/1", "fcm", "token-1"));

            var retained = await fixture.SubscriptionsAsync();
            Assert.Single(retained);
            Assert.NotNull(retained[0].DeleteTime);

            var restored = await fixture.AddAsync(
                new("users/1", "fcm", "token-1", new Dictionary<string, string?> { ["endpoint"] = "beta" }));

            Assert.Equal(created.Uid, restored.Uid);

            var rows = await fixture.SubscriptionsAsync();
            var row  = Assert.Single(rows);
            Assert.Equal(created.Uid, row.Uid);
            Assert.Null(row.DeleteTime);
            Assert.Null(row.PurgeTime);
            Assert.NotNull(row.Metadata);
            Assert.Equal("beta", row.Metadata["endpoint"]);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Add_ForAnActiveTriple_ReturnsTheExistingRow() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            var created = await fixture.AddAsync(
                new("users/1", "fcm", "token-1", new Dictionary<string, string?> { ["endpoint"] = "alpha" }));
            var repeated = await fixture.AddAsync(
                new("users/1", "fcm", "token-1", new Dictionary<string, string?> { ["endpoint"] = "beta" }));

            Assert.Equal(created.Uid, repeated.Uid);

            var rows = await fixture.SubscriptionsAsync();
            var row  = Assert.Single(rows);
            Assert.Null(row.DeleteTime);
            Assert.NotNull(row.Metadata);
            Assert.Equal("alpha", row.Metadata["endpoint"]);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Add_ForADistinctTriple_CreatesASecondRow() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            await fixture.AddAsync(new("users/1", "fcm", "token-1"));
            await fixture.AddAsync(new("users/2", "fcm", "token-1"));

            var rows = await fixture.SubscriptionsAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Null(row.DeleteTime));
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Remove_ForAnActiveTriple_RetainsTheRowSoftDeleted() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            await fixture.AddAsync(new("users/1", "fcm", "token-1"));
            await fixture.RemoveAsync(new("users/1", "fcm", "token-1"));

            var rows = await fixture.SubscriptionsAsync();
            var row  = Assert.Single(rows);
            Assert.NotNull(row.DeleteTime);
        } finally {
            await fixture.DisposeAsync();
        }
    }
}
