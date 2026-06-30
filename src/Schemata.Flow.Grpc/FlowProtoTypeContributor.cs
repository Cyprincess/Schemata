using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc;

namespace Schemata.Flow.Grpc;

/// <summary>Registers Flow process types (definitions, processes, tokens, transitions) for the gRPC transport.</summary>
internal sealed class FlowProtoTypeContributor : IProtoTypeContributor
{
    private static readonly Type[] Summaries = [
        typeof(ProcessDefinitionInfo),
        typeof(SchemataProcess),
        typeof(SchemataProcessToken),
        typeof(SchemataProcessTransition),
    ];

    #region IProtoTypeContributor Members

    public IReadOnlyList<Type> GetSummaryTypes(IServiceProvider serviceProvider) {
        return Summaries;
    }

    #endregion
}
