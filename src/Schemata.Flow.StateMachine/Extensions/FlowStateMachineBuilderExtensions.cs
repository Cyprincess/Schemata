using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.StateMachine.Features;

namespace Schemata.Flow.StateMachine.Extensions;

/// <summary><see cref="SchemataFlowBuilder" /> extensions for the state-machine Flow runtime.</summary>
public static class FlowStateMachineBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataFlowStateMachineFeature" />.</summary>
    public static SchemataFlowBuilder UseStateMachine(this SchemataFlowBuilder builder) {
        builder.AddFeature<SchemataFlowStateMachineFeature>();
        return builder;
    }
}
