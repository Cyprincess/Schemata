using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc;

namespace Schemata.Flow.Grpc;

/// <summary>Registers registry-backed process definitions for the definitions-only gRPC service.</summary>
internal sealed class FlowProtoTypeContributor : IProtoTypeContributor
{
    private static readonly Type[] Summaries = [
        typeof(ProcessDefinitionInfo),
    ];

    #region IProtoTypeContributor Members

    public IReadOnlyList<Type> GetSummaryTypes(IServiceProvider serviceProvider) {
        return Summaries;
    }

    #endregion
}
