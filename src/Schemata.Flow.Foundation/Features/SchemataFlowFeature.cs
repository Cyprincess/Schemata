using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation.Features;

/// <summary>
///     Marks the Flow subsystem as installed. The persisted entity types are chosen by
///     <see cref="SchemataFlowFeature{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" />;
///     this marker only carries the feature ordering contract, so a bridge depending on it never
///     forces the default closed entity types.
/// </summary>
public sealed class SchemataFlowFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Order" /> for the Flow feature.</summary>
    public const int DefaultOrder = DefaultPriority;

    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 90_000_000;

    public override int Order => DefaultOrder;

    public override int Priority => DefaultPriority;
}

/// <summary>
///     Registers the Flow runtime graph closed on the host-selected persisted entity types.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
/// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
/// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
/// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
/// <typeparam name="TEffectIntent">The persisted effect-intent entity type registered by the host.</typeparam>
[DependsOn<SchemataFlowFeature>]
public sealed class SchemataFlowFeature<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> : FeatureBase
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
    where TEffectIntent : SchemataFlowEffectIntent, new()
{
    public override int Order => SchemataFlowFeature.DefaultOrder;

    public override int Priority => SchemataFlowFeature.DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>();
        services.AddSchemataFlowRepositoryEffectRecorder<TEffectIntent>();
    }
}
