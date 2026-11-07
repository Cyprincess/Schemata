using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Tests.Fixtures;
using Xunit;

namespace Schemata.Push.Tests;

[Trait("Category", "Integration")]
public abstract class PushSubscriptionRecreateShould
{
    protected abstract IPushSubscriptionFixture CreateFixture(
        bool nameAdvisor = true,
        bool blockCreate = false,
        bool blockUndelete = false);

    [Fact]
    public async Task Recreate_ForARetainedTriple_UndeletesTheExistingRow() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            var created = await fixture.AddAsync(
                new("users/1", "fcm", "token-1", new Dictionary<string, string?> { ["endpoint"] = "alpha" }));
            Assert.NotNull(created);
            await fixture.RemoveAsync(new("users/1", "fcm", "token-1"));

            var retained = await fixture.SubscriptionsAsync();
            Assert.Single(retained);
            Assert.NotNull(retained[0].DeleteTime);

            var restored = await fixture.AddAsync(
                new("users/1", "fcm", "token-1", new Dictionary<string, string?> { ["endpoint"] = "beta" }));

            Assert.NotNull(restored);
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
    public async Task Add_WhenCreateIsBlocked_ReturnsNullWithoutPersistingARow() {
        var fixture = CreateFixture(blockCreate: true);
        await fixture.InitializeAsync();
        try {
            var added = await fixture.AddAsync(new("owners/alice", "blocked", "endpoint-1"));

            Assert.Null(added);
            var rows = await fixture.SubscriptionsAsync();
            Assert.Empty(rows);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Recreate_WhenUndeleteIsBlocked_ReturnsNullAndRetainsDeletion() {
        var fixture = CreateFixture(blockUndelete: true);
        await fixture.InitializeAsync();
        try {
            var created = await fixture.AddAsync(new("owners/alice", "blocked", "endpoint-1"));
            Assert.NotNull(created);
            await fixture.RemoveAsync(new("owners/alice", "blocked", "endpoint-1"));
            fixture.BlockUndelete();

            var restored = await fixture.AddAsync(new("owners/alice", "blocked", "endpoint-1"));

            Assert.Null(restored);
            var row = Assert.Single(await fixture.SubscriptionsAsync());
            Assert.Equal(created.Uid, row.Uid);
            Assert.NotNull(row.DeleteTime);
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

            Assert.NotNull(created);
            Assert.NotNull(repeated);
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

    [Fact]
    public async Task Add_WithoutANameAdvisor_FailsWithTheMissingNameContract() {
        var fixture = CreateFixture(nameAdvisor: false);
        await fixture.InitializeAsync();
        try {
            var exception = await Assert.ThrowsAsync<ValidationException>(() =>
                fixture.AddAsync(new("users/1", "fcm", "token-1")));

            var violations = exception.Details!.OfType<BadRequestDetail>().Single().FieldViolations;
            Assert.NotNull(violations);
            var violation = Assert.Single(violations);
            Assert.Equal("name", violation.Field);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Add_WithANameAdvisor_UsesTheAdvisorChosenName() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            var created = await fixture.AddAsync(new("users/1", "fcm", "token-1"));

            Assert.NotNull(created);
            Assert.Equal("subscription-1", created.Name);
            Assert.Equal("pushSubscriptions/subscription-1", created.CanonicalName);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentAdds_ForANewTriple_LeaveOneDurableRow() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            var attempts = await Task.WhenAll(
                TryAddAsync(fixture, new("users/1", "fcm", "token-1", new() { ["endpoint"] = "first" })),
                TryAddAsync(fixture, new("users/1", "fcm", "token-1", new() { ["endpoint"] = "second" })));

            Assert.All(attempts, AssertAllowedConflict);
            var row = Assert.Single(await fixture.SubscriptionsAsync());
            Assert.Equal("users/1", row.Owner);
            Assert.Equal("fcm", row.Provider);
            Assert.Equal("token-1", row.ProviderKey);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentAdds_ForARetainedTriple_LeaveOneRestoredRow() {
        var fixture = CreateFixture();
        await fixture.InitializeAsync();
        try {
            await fixture.AddAsync(new("users/1", "fcm", "token-1"));
            await fixture.RemoveAsync(new("users/1", "fcm", "token-1"));

            var attempts = await Task.WhenAll(
                TryAddAsync(fixture, new("users/1", "fcm", "token-1", new() { ["endpoint"] = "first" })),
                TryAddAsync(fixture, new("users/1", "fcm", "token-1", new() { ["endpoint"] = "second" })));

            Assert.All(attempts, AssertAllowedConflict);
            var row = Assert.Single(await fixture.SubscriptionsAsync());
            Assert.Null(row.DeleteTime);
            Assert.Null(row.PurgeTime);
        } finally {
            await fixture.DisposeAsync();
        }
    }

    private static async Task<Exception?> TryAddAsync(IPushSubscriptionFixture fixture, AddPushSubscriptionRequest request) {
        try {
            await fixture.AddAsync(request);
            return null;
        } catch (Exception exception) {
            return exception;
        }
    }

    private static void AssertAllowedConflict(Exception? error) {
        Assert.True(error is null or AlreadyExistsException or AbortedException, error?.ToString());
    }
}
