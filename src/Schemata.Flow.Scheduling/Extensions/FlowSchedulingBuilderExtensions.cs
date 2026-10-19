using Schemata.Core;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Scheduling.Features;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" /> extensions for the Flow.Scheduling bridge.</summary>
public static class FlowSchedulingBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataFlowSchedulingFeature{TJob,TExecution}" /> for the default scheduling entities.</summary>
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseScheduling<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        return builder.UseScheduling<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent, SchemataJob, SchemataJobExecution>();
    }

    /// <summary>Registers <see cref="SchemataFlowSchedulingFeature{TJob,TExecution}" /> for the host-selected scheduling entities.</summary>
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseScheduling<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent, TJob, TExecution>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new()
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        builder.AddFeature<SchemataFlowSchedulingFeature<TJob, TExecution>>();
        return builder;
    }
}
