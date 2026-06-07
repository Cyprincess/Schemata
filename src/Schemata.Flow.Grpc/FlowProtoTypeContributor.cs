using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc;

namespace Schemata.Flow.Grpc;

/// <summary>
///     Registers Flow's entities and trait-bearing request DTOs with
///     <c>SchemataTransportGrpcFeature</c> so the Schemata wire conventions apply
///     against <c>RuntimeTypeModel.Default</c>.
/// </summary>
internal sealed class FlowProtoTypeContributor : IProtoTypeContributor
{
    private static readonly Type[] Summaries = [
        typeof(SchemataProcess),
        typeof(SchemataProcessTransition),
        typeof(ProcessDefinitionInfo),
    ];

    private static readonly Type[] Messages = [
        typeof(CompleteActivityRequest),
        typeof(CorrelateMessageRequest),
    ];

    #region IProtoTypeContributor Members

    public IReadOnlyList<Type> GetSummaryTypes(IServiceProvider serviceProvider) {
        return Summaries;
    }

    public IReadOnlyList<Type> GetMessageTypes(IServiceProvider serviceProvider) {
        return Messages;
    }

    #endregion
}
