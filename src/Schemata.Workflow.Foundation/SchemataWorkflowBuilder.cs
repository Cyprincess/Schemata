using Schemata.Core;

namespace Schemata.Workflow.Foundation;

public sealed class SchemataWorkflowBuilder(Services services)
{
    public Services Services => services;
}
