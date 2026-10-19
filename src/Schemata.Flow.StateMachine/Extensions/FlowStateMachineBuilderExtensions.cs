using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.StateMachine.Features;

namespace Schemata.Flow.StateMachine.Extensions;

/// <summary><see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" /> extensions for the state-machine Flow runtime.</summary>
public static class FlowStateMachineBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataFlowStateMachineFeature{TProcess,TToken,TTransition}" />.</summary>
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseStateMachine<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowStateMachineFeature<TProcess, TToken, TTransition>>();
        return builder;
    }
}
