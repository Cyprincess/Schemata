using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Messaging.RabbitMq;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Transport.RabbitMq;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantReceiverShould
{
    [Fact]
    public async Task Actual_Request_Consumer_Uses_Tenant_Handler_And_Rejects_Malformed_Redelivery() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var calls = new List<Guid>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddDbContextFactory<TenantStorageShould.Database>(options => options.UseSqlite(database).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<TenantStorageShould.Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<TenantStorageShould.Database, SchemataTenantHost>>();
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging();
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddScoped<IRequestHandler<Probe, string>, Handler>()));
        await using var root = services.BuildServiceProvider();
        var tenant = new SchemataTenant { Name = "receiver" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<TenantStorageShould.Database>().Database.EnsureCreatedAsync();
            await setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>().CreateAsync(tenant, default);
        }
        var replies = new List<string>();
        var channel = new Mock<IChannel>();
        channel.Setup(value => value.BasicPublishAsync<BasicProperties>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>((_, _, _, _, body, _) => replies.Add(Encoding.UTF8.GetString(body.Span)))
            .Returns(ValueTask.CompletedTask);
        var type = typeof(RabbitMqRequestOptions).Assembly.GetType("Schemata.Messaging.RabbitMq.Runtime.RabbitMqRequestConsumerHost")!;
        using var consumer = (IDisposable)Activator.CreateInstance(type,
            Options.Create(new RabbitMqRequestOptions().Register<Probe, string>("probe")), null,
            root.GetRequiredService<IMessageExecutionScopeFactory>(), null, null)!;
        type.GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(consumer, channel.Object);
        var receive = type.GetMethod("HandleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        async Task Deliver(string? identity, bool redelivered) {
            var delivery = new BasicDeliverEventArgs("consumer", 1, redelivered, "exchange", "probe",
                new BasicProperties { ReplyTo = "reply", CorrelationId = "correlation", Headers = MessageContextHeaders.Write(new Dictionary<string, string?> { [MessageContexts.TenantIdKey] = identity }) },
                Encoding.UTF8.GetBytes("{}"));
            await (Task)receive.Invoke(consumer, [delivery, CancellationToken.None])!;
        }
        await Deliver(tenant.Uid.ToString("D"), false);
        await Deliver(tenant.Uid.ToString("D"), true);
        await Deliver(null, true);
        Assert.Equal(new[] { tenant.Uid, tenant.Uid }, calls);
        Assert.Equal($"\"{tenant.Uid:D}\"", replies[0]);
        Assert.Contains("internal", replies[2]);
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
    }

    public sealed record Probe : IRequest<string>;
    public sealed class Handler(SchemataTenant tenant, List<Guid> calls) : IRequestHandler<Probe, string>
    {
        public async Task<string> HandleAsync(Probe request, CancellationToken ct) {
            await Task.Yield();
            Assert.Equal(tenant.Uid, TenantContext.Current.Uid);
            calls.Add(tenant.Uid);
            return tenant.Uid.ToString("D");
        }
    }
}
