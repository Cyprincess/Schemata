using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation.Internal;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class InProcessActorTurnScopeFactoryShould
{
    [Fact]
    public async Task CreateAsync_WhenAPropagatorFailsToRestore_DisposesTheScopeBeforeRethrowing() {
        var services = new ServiceCollection();
        services.AddSingleton<DisposalRegistry>();
        services.AddScoped<DisposableProbe>();
        services.AddScoped<IMessageContextPropagator, ThrowingPropagator>();
        var root = services.BuildServiceProvider();

        var factory = new InProcessActorTurnScopeFactory(root.GetRequiredService<IServiceScopeFactory>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(context: null).AsTask());

        var registry = root.GetRequiredService<DisposalRegistry>();
        Assert.Single(registry.Instances);
        Assert.True(registry.Instances[0].Disposed);
    }

    private sealed class DisposalRegistry
    {
        public List<DisposableProbe> Instances { get; } = [];
    }

    private sealed class DisposableProbe : IDisposable
    {
        public DisposableProbe(DisposalRegistry registry) {
            registry.Instances.Add(this);
        }

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>Forces every DisposableProbe in the scope to be instantiated by resolving one, then always fails to restore.</summary>
    private sealed class ThrowingPropagator : IMessageContextPropagator
    {
        public ThrowingPropagator(DisposableProbe probe) { }

        public void Capture(IDictionary<string, string?> items, IServiceProvider source) { }

        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default)
            => throw new InvalidOperationException("restore failed");
    }
}
