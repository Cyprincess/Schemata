using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Moq;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class MessageExecutionScopeFactoryShould
{
    [Fact]
    public async Task CreateAsync_WhenAPropagatorFailsToRestore_DisposesTheScopeBeforeRethrowing() {
        var services = new ServiceCollection();
        services.AddSingleton<DisposalRegistry>();
        services.AddScoped<DisposableProbe>();
        services.AddScoped<IMessageContextPropagator, ThrowingPropagator>();
        using var root = services.BuildServiceProvider();

        var factory = new MessageExecutionScopeFactory(root.GetRequiredService<IServiceScopeFactory>());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            var message = new MessageContext(new Dictionary<string, string?>());
            var scope = await factory.CreateAsync(message);
            using var identity = scope.Enter();
            await using var owned = scope;
            await scope.RestoreAsync(message);
        });

        var registry = root.GetRequiredService<DisposalRegistry>();
        Assert.Single(registry.Instances);
        Assert.True(registry.Instances[0].Disposed);
    }

    [Fact]
    public async Task Failed_Delivery_Restore_Preserves_Activation_And_Subsequent_Ask() {
        var failure = new InvalidOperationException("delivery restore");
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> items, IServiceProvider _, CancellationToken _) =>
                items.ContainsKey("invalid") ? ValueTask.FromException(failure) : ValueTask.CompletedTask);
        var (system, _, root) = ActorSystemFactory.Create(services => services.AddScoped(_ => propagator.Object));
        using var owned = (ServiceProvider)root;
        var actor = await system.SpawnAsync(new("restore", "same"), new(typeof(IdentityActor)));
        var before = await actor.AskAsync<WhoAmI, Guid>(new());
        var context = new MessageContext(new Dictionary<string, string?> { ["invalid"] = "yes" });
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<WhoAmI, Guid>(new(), context).AsTask()));
        Assert.Equal(before, await actor.AskAsync<WhoAmI, Guid>(new()));
        await system.StopAsync(actor.Id);
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
