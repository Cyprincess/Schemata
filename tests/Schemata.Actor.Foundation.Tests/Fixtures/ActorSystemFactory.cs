using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation.Runtime;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Builds a fresh, self-contained <see cref="InProcessActorSystem" /> over a real DI container, so tests exercise the production turn-scope factory rather than a stub.</summary>
public static class ActorSystemFactory
{
    public static (InProcessActorSystem System, ActorRegistry Registry, IServiceProvider Root) Create(Action<IServiceCollection>? configureServices = null) {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var root = services.BuildServiceProvider();

        var registry         = new ActorRegistry();
        var turnScopeFactory = new InProcessActorTurnScopeFactory(root.GetRequiredService<IServiceScopeFactory>());
        var system            = new InProcessActorSystem(root, registry, turnScopeFactory, Options.Create(new SchemataActorOptions()));

        return (system, registry, root);
    }
}