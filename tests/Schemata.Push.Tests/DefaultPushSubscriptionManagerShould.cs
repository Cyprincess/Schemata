using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Push.Foundation;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests;

public class DefaultPushSubscriptionManagerShould
{
    [Fact]
    public async Task Add_Exact_Triple_Exists_Returns_Existing_Without_Writing() {
        var exact = Subscription("owners/one", "email", "one");
        exact.Uid           = Guid.NewGuid();
        exact.Name          = "one";
        exact.CanonicalName = "pushSubscriptions/one";
        exact.Metadata      = new() { ["locale"] = "en" };
        exact.Timestamp     = Guid.NewGuid();
        exact.DisplayName   = "Primary";
        exact.DisplayNames  = new() { ["en"] = "Primary" };
        exact.Description   = "Description";
        exact.Descriptions  = new() { ["en"] = "Description" };
        exact.DeleteTime    = new DateTime(2025, 1, 1, 1, 2, 3, DateTimeKind.Utc);
        exact.PurgeTime     = new DateTime(2025, 2, 1, 1, 2, 3, DateTimeKind.Utc);
        exact.CreateTime    = new DateTime(2024, 12, 1, 1, 2, 3, DateTimeKind.Utc);
        exact.UpdateTime    = new DateTime(2024, 12, 2, 1, 2, 3, DateTimeKind.Utc);
        var rows = new[] {
            Subscription("owners/two", "email", "one"),
            Subscription("owners/one", "sms", "one"),
            Subscription("owners/one", "email", "two"),
            exact,
        };
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.SingleOrDefaultAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>> query,
                            CancellationToken _) =>
                      ValueTask.FromResult<SchemataPushSubscription?>(query(rows.AsQueryable()).SingleOrDefault()));
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        var result = await manager.AddAsync("owners/one", "email", "one");

        Assert.Equal(exact.Uid, result.Uid);
        Assert.Equal(exact.Name, result.Name);
        Assert.Equal(exact.CanonicalName, result.CanonicalName);
        Assert.Equal(exact.Owner, result.Owner);
        Assert.Equal(exact.Provider, result.Provider);
        Assert.Equal(exact.ProviderKey, result.ProviderKey);
        Assert.Equal(exact.Metadata, result.Metadata);
        Assert.Equal(exact.Timestamp, result.Timestamp);
        Assert.Equal(exact.DisplayName, result.DisplayName);
        Assert.Equal(exact.DisplayNames, result.DisplayNames);
        Assert.Equal(exact.Description, result.Description);
        Assert.Equal(exact.Descriptions, result.Descriptions);
        Assert.Equal(exact.DeleteTime, result.DeleteTime);
        Assert.Equal(exact.PurgeTime, result.PurgeTime);
        Assert.Equal(exact.CreateTime, result.CreateTime);
        Assert.Equal(exact.UpdateTime, result.UpdateTime);
        repository.Verify(value => value.AddAsync(
                              It.IsAny<SchemataPushSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_Exact_Triple_Missing_Checks_Then_Adds_And_Commits() {
        var sequence   = new MockSequence();
        var repository = new Mock<IRepository<SchemataPushSubscription>>(MockBehavior.Strict);
        repository.InSequence(sequence)
                  .Setup(value => value.SingleOrDefaultAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>> query,
                            CancellationToken _) =>
                      ValueTask.FromResult<SchemataPushSubscription?>(query(new[] {
                          Subscription("owners/two", "email", "one"),
                          Subscription("owners/one", "sms", "one"),
                          Subscription("owners/one", "email", "two"),
                      }.AsQueryable()).SingleOrDefault()));
        repository.InSequence(sequence)
                  .Setup(value => value.AddAsync(
                             It.Is<SchemataPushSubscription>(subscription =>
                                 subscription.Owner == "owners/one"
                              && subscription.Provider == "email"
                              && subscription.ProviderKey == "one"),
                             It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.InSequence(sequence)
                  .Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        var result = await manager.AddAsync("owners/one", "email", "one");

        Assert.Equal("owners/one", result.Owner);
        Assert.Equal("email", result.Provider);
        Assert.Equal("one", result.ProviderKey);
        repository.VerifyAll();
    }

    [Fact]
    public async Task Exists_Queries_Exact_Triple() {
        var rows = new[] {
            Subscription("owners/one", "email", "two"),
            Subscription("owners/one", "email", "one"),
        };
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.AnyAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>> query,
                            CancellationToken _) => new ValueTask<bool>(query(rows.AsQueryable()).Any()));
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        Assert.True(await manager.ExistsAsync("owners/one", "email", "one"));
        Assert.False(await manager.ExistsAsync("owners/one", "sms", "one"));
    }

    [Fact]
    public async Task Remove_Queries_Exact_Triple_Then_Removes_And_Commits() {
        var exact = Subscription("owners/one", "email", "one");
        var rows = new[] {
            Subscription("owners/one", "email", "two"),
            exact,
        };
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.FirstOrDefaultAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>> query,
                            CancellationToken _) =>
                      ValueTask.FromResult<SchemataPushSubscription?>(query(rows.AsQueryable()).FirstOrDefault()));
        repository.Setup(value => value.RemoveAsync(exact, It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        await manager.RemoveAsync("owners/one", "email", "one");

        repository.Verify(value => value.RemoveAsync(exact, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Exact_Triple_Missing_Does_Not_Write() {
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.FirstOrDefaultAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<SchemataPushSubscription?>(null));
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        await manager.RemoveAsync("owners/one", "email", "one");

        repository.Verify(value => value.RemoveAsync(
                              It.IsAny<SchemataPushSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetForOwner_Applies_Owner_And_Optional_Provider() {
        var rows = new[] {
            Subscription("owners/one", "email", "one"),
            Subscription("owners/one", "sms", "two"),
            Subscription("owners/two", "email", "three"),
        };
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.ListAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>> query,
                            CancellationToken ct) => ToAsync(query(rows.AsQueryable()).ToList(), ct));
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        var all = await CollectAsync(manager.GetForOwnerAsync("owners/one"));
        var email = await CollectAsync(manager.GetForOwnerAsync("owners/one", "email"));

        Assert.Equal(2, all.Count);
        Assert.Single(email);
        Assert.Equal("one", email[0].ProviderKey);
    }

    [Fact]
    public async Task GetForOwner_Materializes_Repository_Rows_Before_Consumer_Exits() {
        var stream = new TrackingAsyncEnumerable([
            Subscription("owners/one", "email", "one"),
            Subscription("owners/one", "email", "two"),
            Subscription("owners/one", "email", "three"),
        ]);
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.ListAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(stream);
        using var services = BuildServices(repository.Object);
        var manager = services.GetRequiredService<IPushSubscriptionManager>();

        await foreach (var _ in manager.GetForOwnerAsync("owners/one")) {
            break;
        }

        Assert.Equal(4, stream.MoveNextCalls);
    }

    private static ServiceProvider BuildServices(IRepository<SchemataPushSubscription> repository) {
        return new ServiceCollection()
              .AddSingleton(repository)
              .AddSchemataPush()
              .BuildServiceProvider();
    }

    private static SchemataPushSubscription Subscription(string owner, string provider, string key) {
        return new() { Owner = owner, Provider = provider, ProviderKey = key };
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source) {
        var result = new List<T>();
        await foreach (var item in source) {
            result.Add(item);
        }

        return result;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(
        IEnumerable<T>                           source,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask;
    }

    private sealed class TrackingAsyncEnumerable(IReadOnlyList<SchemataPushSubscription> rows)
        : IAsyncEnumerable<SchemataPushSubscription>, IAsyncEnumerator<SchemataPushSubscription>
    {
        private int _index = -1;

        public int MoveNextCalls { get; private set; }

        public SchemataPushSubscription Current => rows[_index];

        public IAsyncEnumerator<SchemataPushSubscription> GetAsyncEnumerator(CancellationToken ct = default) {
            return this;
        }

        public ValueTask<bool> MoveNextAsync() {
            MoveNextCalls++;
            return ValueTask.FromResult(++_index < rows.Count);
        }

        public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }
    }
}
