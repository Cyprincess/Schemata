using Schemata.Core;

namespace Schemata.Workflow.Foundation;

public sealed class SchemataWorkflowBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;
}
