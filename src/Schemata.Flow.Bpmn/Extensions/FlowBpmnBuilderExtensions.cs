using Schemata.Flow.Bpmn.Features;
using Schemata.Flow.Foundation.Builders;

namespace Schemata.Flow.Bpmn.Extensions;

/// <summary><see cref="SchemataFlowBuilder" /> extensions for the BPMN Flow runtime.</summary>
public static class FlowBpmnBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataFlowBpmnFeature" />.</summary>
    public static SchemataFlowBuilder UseBpmn(this SchemataFlowBuilder builder) {
        builder.AddFeature<SchemataFlowBpmnFeature>();
        return builder;
    }
}
