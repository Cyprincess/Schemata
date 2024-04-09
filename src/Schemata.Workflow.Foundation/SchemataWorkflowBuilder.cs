using Schemata.Core;

namespace Schemata.Workflow.Foundation;

public class SchemataWorkflowBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;
}
