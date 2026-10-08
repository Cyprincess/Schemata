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
using Moq;
using RabbitMQ.Client;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Messaging.RabbitMq;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Transport.RabbitMq;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

/// <summary>
///     Verifies that a tenant scope binds the public dispatcher interfaces and the concrete
///     <see cref="InProcessRequestDispatcher" /> to the same owner: in-process hosts get a
///     tenant-bound dispatcher whose handlers and advisors resolve tenant-side, while a host whose
///     dispatcher slots belong to a broker transport keeps publishing outbound through that
///     transport and uses the in-process dispatcher only for inbound consumption.
/// </summary>
[Trait("Layer", "Integration")]
public class TenantDispatcherShould
{
    [Fact]
    public async Task Bind_Public_Interfaces_To_The_Tenant_Dispatcher_When_The_Host_Dispatches_In_Process() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var handled = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(handled);
        services.AddDbContextFactory<TenantStorageShould.Database>(options => options.UseSqlite(database).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<TenantStorageShould.Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<TenantStorageShould.Database, SchemataTenantHost>>();
        services.AddInProcessRequestDispatcher();
        services.AddSingleton<IRequestHandler<HostProbe, string>>(new HostProbeHandler(handled));
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging();
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddScoped<IRequestHandler<TenantProbe, string>, TenantProbeHandler>()));
        await using var root = services.BuildServiceProvider();
        var (tenantA, tenantB) = await CreateTenantsAsync(root, "tenant-a", "tenant-b");
        var factory = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();

        await using var scopeA = await factory.CreateAsync(new(tenantA.Uid));
        var requestDispatcher = scopeA.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        Assert.Same(requestDispatcher, scopeA.ServiceProvider.GetRequiredService<ICommandDispatcher>());
        Assert.Same(requestDispatcher, scopeA.ServiceProvider.GetRequiredService<IQueryDispatcher>());
        Assert.Same(requestDispatcher, scopeA.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>());

        var resultA = await requestDispatcher.SendAsync<TenantProbe, string>(new());
        Assert.Equal("tenant-a", resultA);

        Assert.NotNull(tenantB);

        await using var scopeB = await factory.CreateAsync(new(tenantB.Uid));
        var resultB = await scopeB.ServiceProvider.GetRequiredService<IRequestDispatcher>().SendAsync<TenantProbe, string>(new());
        Assert.Equal("tenant-b", resultB);

        // Host-registered handlers stay visible to tenant callers through the composite scope.
        var hostViaTenant = await requestDispatcher.SendAsync<HostProbe, string>(new());
        Assert.Equal("host", hostViaTenant);

        await using var hostScope = root.CreateAsyncScope();
        var hostResult = await hostScope.ServiceProvider.GetRequiredService<IRequestDispatcher>().SendAsync<HostProbe, string>(new());
        Assert.Equal("host", hostResult);

        // The host dispatcher never sees tenant-registered handlers.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hostScope.ServiceProvider.GetRequiredService<IRequestDispatcher>().SendAsync<TenantProbe, string>(new()));

        Assert.Equal(["tenant-a", "tenant-b", "host", "host"], handled);
    }

    [Fact]
    public async Task Reject_Dispatch_When_Host_And_Tenant_Both_Register_The_Exclusive_Handler() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<TenantStorageShould.Database>(options => options.UseSqlite(database).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<TenantStorageShould.Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<TenantStorageShould.Database, SchemataTenantHost>>();
        services.AddInProcessRequestDispatcher();
        services.AddSingleton<IRequestHandler<HostProbe, string>>(new HostProbeHandler());
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging();
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddSingleton<IRequestHandler<HostProbe, string>>(new HostProbeHandler())));
        await using var root = services.BuildServiceProvider();
        var (tenantA, _) = await CreateTenantsAsync(root, "tenant-a");
        var factory = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();

        await using var scope = await factory.CreateAsync(new(tenantA.Uid));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().SendAsync<HostProbe, string>(new()));
        Assert.Contains("Multiple request handlers", ex.Message);
    }

    [Fact]
    public async Task Keep_Broker_Owned_Public_Interfaces_When_RabbitMQ_Owns_The_Host_Slots() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();

        string? observedExchange   = null;
        string? observedRoutingKey = null;

        var channel = new Mock<IChannel>();
        channel.Setup(c => c.QueueDeclareAsync(
                       It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                       It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                       It.IsAny<CancellationToken>()))
               .ReturnsAsync(new QueueDeclareOk("reply", 0, 0));
        channel.Setup(c => c.BasicConsumeAsync(
                       It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                       It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>>(),
                       It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("consumer-tag");
        channel.Setup(c => c.ExchangeDeclareAsync(
                       It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                       It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                       It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        channel.Setup(c => c.CloseAsync(It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var connection = new Mock<IConnection>();
        connection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(channel.Object);

        var services = new ServiceCollection();
        var handled = new List<string>();
        services.AddSingleton(handled);
        services.AddDbContextFactory<TenantStorageShould.Database>(options => options.UseSqlite(database).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<TenantStorageShould.Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<TenantStorageShould.Database, SchemataTenantHost>>();

        var connections = new Mock<IRabbitMqConnectionProvider>();
        connections.Setup(p => p.GetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        services.AddSingleton(connections.Object);
        services.AddRabbitMqTransport();
        services.AddRabbitMqRequestDispatcher(options => options.Register<TenantProbe, string>("tenant-probe"));
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging();
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddScoped<IRequestHandler<TenantProbe, string>, TenantProbeHandler>()));
        await using var root = services.BuildServiceProvider();

        channel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                       It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                       It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                   (exchange, routingKey, _, properties, _, _) => {
                       observedExchange   = exchange;
                       observedRoutingKey = routingKey;
                       var correlationId = properties.CorrelationId
                        ?? throw new InvalidOperationException("The dispatcher must publish a correlation id.");
                       root.GetRequiredService<CorrelationTracker>().Complete(correlationId, "broker");
                   })
               .Returns(ValueTask.CompletedTask);

        var (tenantA, _) = await CreateTenantsAsync(root, "tenant-a");
        var factory = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();

        // No live broker: the channel mock is the transport boundary, and the reply is completed
        // through the same CorrelationTracker the dispatcher awaits.
        await using var scope = await factory.CreateAsync(new(tenantA.Uid));
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        Assert.IsNotType<InProcessRequestDispatcher>(dispatcher);

        var reply = await dispatcher.SendAsync<TenantProbe, string>(new());

        Assert.Equal("broker", reply);
        Assert.Equal("schemata.requests", observedExchange);
        Assert.Equal("tenant-probe", observedRoutingKey);
        Assert.Empty(handled);

        // The receiver side still resolves the tenant-bound in-process concrete.
        Assert.IsType<InProcessRequestDispatcher>(scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>());
    }

    private static async Task<(SchemataTenant A, SchemataTenant? B)> CreateTenantsAsync(
        IServiceProvider root,
        string           first,
        string?          second = null
    ) {
        var tenantA = new SchemataTenant { Name = first };
        var tenantB = second is null ? null : new SchemataTenant { Name = second };
        await using var setup = root.CreateAsyncScope();
        await setup.ServiceProvider.GetRequiredService<TenantStorageShould.Database>().Database.EnsureCreatedAsync();
        var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
        await manager.CreateAsync(tenantA, default);
        if (tenantB is not null) {
            await manager.CreateAsync(tenantB, default);
        }

        return (tenantA, tenantB);
    }

    public sealed record TenantProbe : IRequest<string>;

    public sealed record HostProbe : IRequest<string>;

    public sealed class TenantProbeHandler(SchemataTenant tenant, List<string> handled) : IRequestHandler<TenantProbe, string>
    {
        public Task<string> HandleAsync(TenantProbe request, CancellationToken ct) {
            handled.Add(tenant.Name!);
            return Task.FromResult(tenant.Name!);
        }
    }

    public sealed class HostProbeHandler(List<string>? handled = null) : IRequestHandler<HostProbe, string>
    {
        public Task<string> HandleAsync(HostProbe request, CancellationToken ct) {
            handled?.Add("host");
            return Task.FromResult("host");
        }
    }
}
