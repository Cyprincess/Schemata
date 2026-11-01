using Schemata.Flow.Bpmn.Features;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Bpmn.Extensions;

/// <summary><see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" /> extensions for the BPMN Flow runtime.</summary>
public static class FlowBpmnBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataFlowBpmnFeature{TProcess,TToken,TTransition}" />.</summary>
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseBpmn<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowBpmnFeature<TProcess, TToken, TTransition>>();
        return builder;
    }
}
