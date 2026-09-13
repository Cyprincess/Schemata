using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc;

namespace Schemata.Flow.Grpc;

/// <summary>Registers Flow process types (definitions, processes, tokens, transitions) for the gRPC transport.</summary>
internal sealed class FlowProtoTypeContributor : IProtoTypeContributor
{
    private static readonly (Type Identity, Type Item)[] ListTypes = [
        (typeof(ProcessDefinitionInfo), typeof(ProcessDefinitionInfo)),
        (typeof(SchemataProcess), typeof(SchemataProcess)),
        (typeof(SchemataProcessToken), typeof(SchemataProcessToken)),
        (typeof(SchemataProcessTransition), typeof(SchemataProcessTransition)),
    ];

    #region IProtoTypeContributor Members

    public IReadOnlyList<(Type Identity, Type Item)> GetListTypes(IServiceProvider serviceProvider) {
        return ListTypes;
    }

    #endregion
}
