using Schemata.Core;
using Schemata.Workflow.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseWorkflow(this SchemataBuilder builder) {
        builder.AddFeature<SchemataWorkflowFeature>();

        return builder;
    }
}
