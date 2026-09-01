using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Actor.Foundation.Features;

/// <summary>Registers the in-process actor system: the actor registry, mailbox host, and default turn-scope factory.</summary>
public sealed class SchemataActorFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Order" /> for the Actor feature.</summary>
    public const int DefaultOrder = DefaultPriority;

    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Actor feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 50_000_000;

    public override int Order => DefaultOrder;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataActor();
}
