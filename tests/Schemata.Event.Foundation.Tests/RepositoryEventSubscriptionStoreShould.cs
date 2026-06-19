using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class RepositoryEventSubscriptionStoreShould
{
    [Fact]
    public async Task AddAsync_PersistsRow() {
        var rows  = new List<SchemataEventSubscription>();
        var store = new RepositoryEventSubscriptionStore(Repository(rows).Object);

        await store.AddAsync(new EventSubscription("sub-1", "invoice.paid", "invoices/1", "processes/1"));

        var row = Assert.Single(rows);
        Assert.Equal("sub-1", row.SubscriptionId);
        Assert.Equal("invoice.paid", row.EventType);
        Assert.Equal("invoices/1", row.CorrelationKey);
        Assert.Equal("processes/1", row.Target);
    }

    [Fact]
    public async Task AddAsync_DuplicateId_Upserts() {
        var rows = new List<SchemataEventSubscription> {
            new() { SubscriptionId = "sub-1", EventType = "old", Target = "old-target" },
        };
        var store = new RepositoryEventSubscriptionStore(Repository(rows).Object);

        await store.AddAsync(new EventSubscription("sub-1", "invoice.paid", null, "processes/1"));

        var row = Assert.Single(rows);
        Assert.Equal("invoice.paid", row.EventType);
        Assert.Null(row.CorrelationKey);
        Assert.Equal("processes/1", row.Target);
    }

    [Fact]
    public async Task RemoveAsync_DeletesRow() {
        var rows = new List<SchemataEventSubscription> {
            new() { SubscriptionId = "sub-1", EventType = "invoice.paid", Target = "processes/1" },
        };
        var store = new RepositoryEventSubscriptionStore(Repository(rows).Object);

        await store.RemoveAsync("sub-1");

        Assert.Empty(rows);
    }

    [Fact]
    public async Task FindAsync_FiltersByEventType() {
        var rows = new List<SchemataEventSubscription> {
            new() { SubscriptionId = "sub-1", EventType = "invoice.paid", Target = "processes/1" },
            new() { SubscriptionId = "sub-2", EventType = "invoice.sent", Target = "processes/2" },
        };
        var store = new RepositoryEventSubscriptionStore(Repository(rows).Object);

        var found = await store.FindAsync("invoice.paid");

        var subscription = Assert.Single(found);
        Assert.Equal("sub-1", subscription.Id);
        Assert.Equal("processes/1", subscription.Target);
    }

    [Fact]
    public async Task FindAsync_WithCorrelationKey_FiltersByBoth() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "sub-1",
                EventType      = "invoice.paid",
                CorrelationKey = "invoices/1",
                Target         = "processes/1",
            },
            new() {
                SubscriptionId = "sub-2",
                EventType      = "invoice.paid",
                CorrelationKey = "invoices/2",
                Target         = "processes/2",
            },
        };
        var store = new RepositoryEventSubscriptionStore(Repository(rows).Object);

        var found = await store.FindAsync("invoice.paid", "invoices/2");

        var subscription = Assert.Single(found);
        Assert.Equal("sub-2", subscription.Id);
        Assert.Equal("invoices/2", subscription.CorrelationKey);
    }

    private static Mock<IRepository<SchemataEventSubscription>> Repository(List<SchemataEventSubscription> rows) {
        var records = new Mock<IRepository<SchemataEventSubscription>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEventSubscription>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEventSubscription row, CancellationToken _) => rows.Add(row))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEventSubscription>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.RemoveAsync(It.IsAny<SchemataEventSubscription>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEventSubscription row, CancellationToken _) => rows.Remove(row))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.FirstOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>,
                              IQueryable<SchemataEventSubscription>>>(), It.IsAny<CancellationToken>()))
               .Returns((
                            Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>
                                predicate,
                            CancellationToken _
                        ) => new(predicate(rows.AsQueryable()).FirstOrDefault()));
        records.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>,
                              IQueryable<SchemataEventSubscription>>>(), It.IsAny<CancellationToken>()))
               .Returns((
                            Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>
                                predicate,
                            CancellationToken _
                        ) => ToAsync(predicate(rows.AsQueryable()).ToList()));
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return records;
    }

    private static async IAsyncEnumerable<SchemataEventSubscription> ToAsync(
        IEnumerable<SchemataEventSubscription> rows
    ) {
        foreach (var row in rows) {
            yield return row;
            await Task.Yield();
        }
    }
}
