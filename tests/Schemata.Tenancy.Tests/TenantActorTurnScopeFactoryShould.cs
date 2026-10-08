using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Tenancy;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

[Trait("Layer", "Integration")]
public class TenantMessageExecutionScopeFactoryShould
{
    [Fact]
    public async Task Final_Scope_Uses_Explicit_Tenant_Constructor_Dependencies_And_Restores_Identity() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid(), DisplayName = "tenant" };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();
        services.AddSingleton<ITenantServiceProviderFactory<SchemataTenant>, SchemataTenantServiceProviderFactory<SchemataTenant>>();
        services.AddSingleton<ITenantServiceScopeFactory<SchemataTenant>, SchemataTenantServiceScopeFactory<SchemataTenant>>();
        services.AddOptions<SchemataTenancyOptions>().Configure(options => options.TenantOverrides[tenant.Uid.ToString()] = [registrations => {
            registrations.AddScoped<Consumer>();
        }]);
        using var root = services.BuildServiceProvider();
        var factory = new TenantMessageExecutionScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var context = MessageContexts.Bind(new(tenant.Uid));
        var first = await factory.CreateAsync(context);
        await Task.Yield();
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        Consumer consumer;
        using (first.Enter()) {
            await using var owned = first;
            await first.RestoreAsync(context);
            consumer = first.Services.GetRequiredService<Consumer>();
            Assert.Equal("tenant", consumer.Name);
            Assert.Equal(new TenantIdentity(tenant.Uid), TenantContext.Current);
            Assert.Same(consumer, first.Services.GetRequiredService<Consumer>());
        }
        Assert.True(consumer.Disposed);
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        var second = await factory.CreateAsync(context);
        using (second.Enter()) {
            await using var owned = second;
            Assert.NotSame(consumer, second.Services.GetRequiredService<Consumer>());
        }
    }

    public sealed class Consumer(SchemataTenant tenant) : IDisposable
    {
        public string? Name => tenant.DisplayName;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
