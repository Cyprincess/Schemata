using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
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
        var disposed = new List<Guid?>();
        services.AddSingleton(disposed);
        services.AddSingleton(manager.Object);
        services.AddScoped(typeof(TenantIdentity), _ => TenantIdentity.Host);
        services.AddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();
        services.AddSingleton<ITenantServiceProviderFactory<SchemataTenant>, SchemataTenantServiceProviderFactory<SchemataTenant>>();
        services.AddSingleton<ITenantServiceScopeFactory<SchemataTenant>, SchemataTenantServiceScopeFactory<SchemataTenant>>();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        services.AddSingleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<SchemataTenant>>();
        services.AddSingleton<IMessageExecutionScopeRunner, MessageExecutionScopeRunner>();
        services.AddOptions<SchemataTenancyOptions>().Configure(options => options.TenantOverrides[tenant.Uid.ToString()] = [registrations => {
            registrations.AddScoped<Consumer>();
            registrations.AddScoped<IMessageContextPropagator, SelectedPropagator>();
        }]);
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var factory = new TenantMessageExecutionScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var context = new MessageContext(new Dictionary<string, string?> { [TenancyConstants.Messaging.TenantIdKey] = tenant.Uid.ToString("D") });
        var captured = await root.GetRequiredService<IMessageExecutionScopeRunner>().RunAsync(context,
            (provider, _) => ValueTask.FromResult(MessageContexts.Capture(provider)));
        Assert.Equal("tenant", captured.Items["test.selected"]);
        Assert.Equal(tenant.Uid.ToString("D"), captured.Items[TenancyConstants.Messaging.TenantIdKey]);
        Assert.Equal(tenant.Uid, Assert.Single(disposed));

        Consumer consumer;
        var first = await factory.CreateAsync(context);
        await using (var owned = first) {
            await first.RestoreAsync(context);
            consumer = first.Services.GetRequiredService<Consumer>();
            Assert.Equal("tenant", consumer.Name);
            Assert.Equal(new TenantIdentity(tenant.Uid), first.Services.GetRequiredService<TenantIdentity>());
            Assert.Same(consumer, first.Services.GetRequiredService<Consumer>());
        }
        Assert.True(consumer.Disposed);
        var second = await factory.CreateAsync(context);
        await using (var owned = second) {
            await second.RestoreAsync(context);
            Assert.NotSame(consumer, second.Services.GetRequiredService<Consumer>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Bootstrap_Disposal_Releases_The_Unreturned_Final_Scope(bool finalCleanupFails) {
        var bootstrapFailure = new InvalidOperationException("bootstrap cleanup");
        var finalFailure = new ApplicationException("final cleanup");
        var selected = new Mock<ITenantServiceScopeFactory<SchemataTenant>>();
        var final = new Mock<IServiceScope>();
        var finalAsync = final.As<IAsyncDisposable>();
        finalAsync.Setup(value => value.DisposeAsync()).Returns(finalCleanupFails
            ? ValueTask.FromException(finalFailure) : ValueTask.CompletedTask);
        selected.Setup(value => value.CreateAsync(TenantIdentity.Host, default)).Returns(new ValueTask<AsyncServiceScope>(new AsyncServiceScope(final.Object)));
        using var root = new ServiceCollection().AddSingleton(selected.Object).BuildServiceProvider();
        var bootstrap = new Mock<IServiceScope>();
        bootstrap.SetupGet(value => value.ServiceProvider).Returns(root);
        bootstrap.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.FromException(bootstrapFailure));
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(value => value.CreateScope()).Returns(bootstrap.Object);
        var factory = new TenantMessageExecutionScopeFactory<SchemataTenant>(scopes.Object);
        if (finalCleanupFails) {
            var error = await Assert.ThrowsAsync<AggregateException>(() => factory.CreateAsync(null).AsTask());
            Assert.Equal(new Exception[] { bootstrapFailure, finalFailure }, error.Flatten().InnerExceptions);
        } else {
            Assert.Same(bootstrapFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(null).AsTask()));
        }
        finalAsync.Verify(value => value.DisposeAsync(), Times.Once);
    }

    public sealed class Consumer(SchemataTenant tenant) : IDisposable
    {
        public string? Name => tenant.DisplayName;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public sealed class SelectedPropagator(Consumer consumer, TenantIdentity identity, List<Guid?> disposed) : IMessageContextPropagator, IAsyncDisposable
    {
        private bool _restored;
        public void Capture(IDictionary<string, string?> items, IServiceProvider source) {
            Assert.True(_restored);
            Assert.Equal(identity, source.GetRequiredService<TenantIdentity>());
            items["test.selected"] = consumer.Name;
        }
        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
            Assert.Equal(identity, target.GetRequiredService<TenantIdentity>());
            _restored = true;
            return ValueTask.CompletedTask;
        }
        public async ValueTask DisposeAsync() {
            await Task.Yield();
            disposed.Add(identity.Uid);
        }
    }
}
