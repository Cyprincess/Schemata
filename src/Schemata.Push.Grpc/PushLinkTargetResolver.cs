using System.Collections.Generic;
using Schemata.Core.Linking;
using Schemata.Push.Skeleton;
using Schemata.Transport.Grpc.Linking;

namespace Schemata.Push.Grpc;

public sealed class PushLinkTargetResolver(PushGrpcModel model) : IGrpcLinkTargetResolver
{
    public GrpcLinkTarget? Resolve(SemanticTarget target) => target is DomainOperationTarget operation
        && operation.Operation == PushConstants.Routes.Subscriptions
        ? new(model.List.FullName, operation.Parameters ?? new Dictionary<string, string?>()) : null;
    public SemanticTarget? GetTarget(string method, object request) => method == model.List.FullName && request is ListPushSubscriptionsQuery query
        ? new DomainOperationTarget(PushConstants.Routes.Subscriptions, new Dictionary<string, string?> { ["provider"] = query.Provider }) : null;
}
