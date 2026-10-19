using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Runtime;
using Schemata.Tenancy.Skeleton.Tenancy;
using Schemata.Transport.RabbitMq;
using Schemata.Tenancy.Messaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantEventShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_Event_Bus_Resolves_And_Disposes_Tenant_Subscriber_Under_Captured_Identity(bool fail) {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observed = new List<Guid>();
        var disposed = new DisposalObservations { Fail = fail };
        var services = new ServiceCollection();
        services.AddScoped<IEventDispatchContext, EventDispatchContext>();
        services.AddSingleton(observed);
        services.AddSingleton(disposed);
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        services.AddRepository<SchemataEventSubscription, EfCoreRepository<Database, SchemataEventSubscription>>();
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseEvent().RegisterEvent<Notice>("notice").UseProducer(producer => producer.UseInProcess());
        builder.Invoke(services);
        services.RemoveAll<IEventLifecycleObserver>();
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddScoped<IEventHandler<Notice>, Subscriber>()));
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging();
        await using var root = services.BuildServiceProvider();
        var tenant = new SchemataTenant { Name = "event" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            await setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>().CreateAsync(tenant, default);
        }
        await using var sender = root.CreateAsyncScope();
        var bus = sender.ServiceProvider.GetRequiredService<IEventBus>();
        using (TenantContext.Enter(new(tenant.Uid))) {
            if (fail) {
                var failure = await Assert.ThrowsAsync<AggregateException>(() => bus.PublishAsync(new Notice()));
                Assert.Equal(new Exception[] { disposed.HandlerFailure, disposed.CleanupFailure }, failure.Flatten().InnerExceptions);
            } else {
                await bus.PublishAsync(new Notice());
            }
        }
        Assert.Equal(tenant.Uid, Assert.Single(observed));
        Assert.Equal(tenant.Uid, Assert.Single(disposed.Identities));
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        await using (var setup = root.CreateAsyncScope()) {
            var subscriptions = setup.ServiceProvider.GetRequiredService<IRepository<SchemataEventSubscription>>();
            await subscriptions.AddAsync(new() { Name = "notice", EventType = "notice", SubscriptionId = "notice", Target = "tenant" });
            await subscriptions.CommitAsync();
        }
        using var receiver = new RabbitMqConsumerHost(root, null!, Options.Create(new RabbitMqEventOptions()), Options.Create(new JsonSerializerOptions()));
        var receive = typeof(RabbitMqConsumerHost).GetMethod("HandleMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<bool> Deliver(string? identity) {
            var message = new BasicDeliverEventArgs("consumer", 1, true, "exchange", "notice", new BasicProperties {
                Headers = MessageContextHeaders.Write(new Dictionary<string, string?> { [TenancyConstants.Messaging.TenantIdKey] = identity }),
            }, Encoding.UTF8.GetBytes("{}"));
            return (Task<bool>)receive.Invoke(receiver, [Mock.Of<IChannel>(), message, CancellationToken.None])!;
        }
        if (fail) {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => Deliver(tenant.Uid.ToString("D")));
            Assert.Equal(new Exception[] { disposed.HandlerFailure, disposed.CleanupFailure }, failure.Flatten().InnerExceptions);
        } else {
            Assert.True(await Deliver(tenant.Uid.ToString("D")));
        }
        await Assert.ThrowsAsync<TenantResolveException>(() => Deliver("malformed"));
        Assert.Equal(new[] { tenant.Uid, tenant.Uid }, observed);
        Assert.Equal(new Guid?[] { tenant.Uid, tenant.Uid }, disposed.Identities);
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
    }

    public sealed record Notice : IEvent;
    public sealed class Subscriber(SchemataTenant tenant, List<Guid> observed, DisposalObservations disposed) : IEventHandler<Notice>, IAsyncDisposable
    {
        public async Task HandleAsync(Notice notice, CancellationToken ct) {
            await Task.Yield();
            Assert.Equal(tenant.Uid, TenantContext.Current.Uid);
            observed.Add(tenant.Uid);
            if (disposed.Fail) throw disposed.HandlerFailure;
        }
        public async ValueTask DisposeAsync() {
            await Task.Yield();
            Assert.Equal(tenant.Uid, TenantContext.Current.Uid);
            disposed.Identities.Add(TenantContext.Current.Uid);
            if (disposed.Fail) throw disposed.CleanupFailure;
        }
    }
    public sealed class DisposalObservations
    {
        public List<Guid?> Identities { get; } = [];
        public bool Fail { get; init; }
        public Exception HandlerFailure { get; } = new InvalidOperationException("event handler");
        public Exception CleanupFailure { get; } = new ApplicationException("event cleanup");
    }
    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            model.Entity<SchemataEventSubscription>();
            base.OnModelCreating(model);
        }
    }
}
