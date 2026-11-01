using Schemata.Flow.Event.Features;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" /> extensions for the Flow.Event bridge.</summary>
public static class FlowEventBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataFlowEventFeature" />.</summary>
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseEvent<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowEventFeature>();
        return builder;
    }
}
