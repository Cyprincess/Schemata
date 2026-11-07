using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantExecutionIdentityShould
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task Parallel_Scopes_Resolve_Their_Own_Identity_And_Final_Services() {
        await using var host = await TenantExecutionHost.CreateAsync();
        var a = await host.CreateScopeAsync(new(TenantA));
        var b = await host.CreateScopeAsync(new(TenantB));
        var first = a.ServiceProvider.GetRequiredService<ExecutionProbe>().ReadAsync();
        var second = b.ServiceProvider.GetRequiredService<ExecutionProbe>().ReadAsync();

        var values = await Task.WhenAll(first, second);

        Assert.Equal(TenantA, values[0].Identity.Uid);
        Assert.Equal(TenantB, values[1].Identity.Uid);
        Assert.Equal(TenantA, values[0].SingletonObservedIdentity.Uid);
        Assert.Equal(TenantB, values[1].SingletonObservedIdentity.Uid);

        await a.DisposeAsync();
        await b.DisposeAsync();
        Assert.Equal([TenantA, TenantB], host.Disposed);

        await using var hostScope = await host.CreateScopeAsync(TenantIdentity.Host);
        Assert.Equal(TenantIdentity.Host, hostScope.ServiceProvider.GetRequiredService<TenantIdentity>());
    }

    [Fact]
    public async Task RestoreAsync_Rejects_A_Different_Target_Identity() {
        await using var host = await TenantExecutionHost.CreateAsync();
        await using var a = await host.CreateScopeAsync(new(TenantA));
        await using var b = await host.CreateScopeAsync(new(TenantB));
        var propagator = new TenantMessageContextPropagator();
        var items = new Dictionary<string, string?>();
        propagator.Capture(items, a.ServiceProvider);

        await Assert.ThrowsAsync<TenantResolveException>(() => propagator.RestoreAsync(items, b.ServiceProvider).AsTask());
    }

    public sealed record Observation(TenantIdentity Identity, TenantIdentity SingletonObservedIdentity);

    public sealed class ExecutionProbe(TenantIdentity identity, IServiceProvider services, HostSingleton singleton, List<Guid?> disposed)
        : IAsyncDisposable
    {
        public async Task<Observation> ReadAsync() {
            await Task.Yield();
            return new(identity, singleton.Read(services));
        }

        public async ValueTask DisposeAsync() {
            await Task.Yield();
            disposed.Add(identity.Uid);
        }
    }

    public sealed class HostSingleton
    {
        public TenantIdentity Read(IServiceProvider invocation) => invocation.GetRequiredService<TenantIdentity>();
    }

    private sealed class TenantExecutionHost : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ITenantServiceScopeFactory<SchemataTenant> _scopes;

        private TenantExecutionHost(ServiceProvider services, List<Guid?> disposed) {
            _services = services;
            _scopes = services.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
            Disposed = disposed;
        }

        public List<Guid?> Disposed { get; }

        public static Task<TenantExecutionHost> CreateAsync() {
            var a = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
            var b = new SchemataTenant { Uid = TenantB, Timestamp = Guid.NewGuid() };
            var manager = new Mock<ITenantManager<SchemataTenant>>();
            manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(a);
            manager.Setup(value => value.FindByTenantId(TenantB, It.IsAny<CancellationToken>())).ReturnsAsync(b);
            var disposed = new List<Guid?>();
            var services = new ServiceCollection();
            services.AddScoped(typeof(TenantIdentity), _ => TenantIdentity.Host);
            services.AddSingleton(manager.Object);
            services.AddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();
            services.AddSingleton<ITenantServiceProviderFactory<SchemataTenant>, SchemataTenantServiceProviderFactory<SchemataTenant>>();
            services.AddSingleton<ITenantServiceScopeFactory<SchemataTenant>, SchemataTenantServiceScopeFactory<SchemataTenant>>();
            services.AddScoped<ExecutionProbe>();
            services.AddSingleton<HostSingleton>();
            services.AddSingleton(disposed);
            services.AddOptions<SchemataTenancyOptions>().Configure(options => options.DynamicOverrides.Add((_, overrides, _) => {
                overrides.AddScoped<ExecutionProbe>();
            }));
            return Task.FromResult(new TenantExecutionHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), disposed));
        }

        public ValueTask<AsyncServiceScope> CreateScopeAsync(TenantIdentity identity) => _scopes.CreateAsync(identity);

        public async ValueTask DisposeAsync() {
            await _services.DisposeAsync();
        }
    }
}
