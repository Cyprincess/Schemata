using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class SchemataEventSubscriptionPersistenceShould
{
    [Fact]
    public async Task Persist_And_Read_Subscription_Row_PreservesToken() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SubscriptionDbContext>(options => options.UseSqlite(connection)
                                                                   .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        using var provider = services.BuildServiceProvider();
        await using (var db = await provider.GetRequiredService<IDbContextFactory<SubscriptionDbContext>>().CreateDbContextAsync()) {
            await db.Database.EnsureCreatedAsync();
            await db.Subscriptions.AddAsync(new() {
                Uid = Guid.NewGuid(), Name = "subscription", CanonicalName = "event-subscriptions/subscription",
                SubscriptionId = "flow:processes/p1:wait:processes/p1/tokens/t1", EventType = "payment",
                Target = "processes/p1", Token = "processes/p1/tokens/t1",
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await provider.GetRequiredService<IDbContextFactory<SubscriptionDbContext>>().CreateDbContextAsync();
        var stored = await readDb.Subscriptions.FirstOrDefaultAsync(subscription => subscription.EventType == "payment");
        Assert.Equal("processes/p1/tokens/t1", stored!.Token);
    }

    private sealed class SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataEventSubscription> Subscriptions { get; set; } = null!;
    }
}
