using Schemata.Flow.Actor.Features;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" /> extensions for the Flow.Actor bridge.</summary>
public static class SchemataFlowBuilderActorExtensions
{
    /// <summary>Enables the <see cref="SchemataFlowActorFeature" />.</summary>
    /// <param name="builder">The flow builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseActor<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowActorFeature>();

        return builder;
    }
}
