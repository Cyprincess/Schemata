using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Actor.Foundation.Features;

/// <summary>
///     Marks the actor subsystem as installed. The persisted actor-state entity type is chosen by
///     <see cref="SchemataActorFeature{TActor}" />; this marker only carries the feature ordering
///     contract, so a bridge depending on it never forces the default closed entity type.
/// </summary>
public sealed class SchemataActorFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Order" /> for the Actor feature.</summary>
    public const int DefaultOrder = DefaultPriority;

    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Actor feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 50_000_000;

    public override int Order => DefaultOrder;

    public override int Priority => DefaultPriority;
}

/// <summary>
///     Registers the in-process actor system: the actor registry, mailbox host, and default
///     turn-scope factory, closed on the persisted actor-state entity type
///     <typeparamref name="TActor" />.
/// </summary>
/// <typeparam name="TActor">The persisted actor-state entity type registered by the host.</typeparam>
[DependsOn<SchemataActorFeature>]
public sealed class SchemataActorFeature<TActor> : FeatureBase
    where TActor : SchemataActor, new()
{
    public override int Order => SchemataActorFeature.DefaultOrder;

    public override int Priority => SchemataActorFeature.DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataActor<TActor>();
}
