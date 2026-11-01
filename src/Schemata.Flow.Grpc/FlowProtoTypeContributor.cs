using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc;

namespace Schemata.Flow.Grpc;

/// <summary>Registers Flow process types (definitions, processes, tokens, transitions) for the gRPC transport.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
internal sealed class FlowProtoTypeContributor<TProcess, TToken, TTransition> : IProtoTypeContributor
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
{
    private static readonly (Type Identity, Type Item)[] ListTypes = [
        (typeof(ProcessDefinitionInfo), typeof(ProcessDefinitionInfo)),
        (typeof(TProcess), typeof(TProcess)),
        (typeof(TToken), typeof(TToken)),
        (typeof(TTransition), typeof(TTransition)),
    ];

    #region IProtoTypeContributor Members

    public IReadOnlyList<(Type Identity, Type Item)> GetListTypes(IServiceProvider serviceProvider) {
        return ListTypes;
    }

    #endregion
}
