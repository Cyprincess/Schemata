using System.Collections.Generic;
using Schemata.Core.Linking;
using Schemata.Insight.Skeleton;
using Schemata.Transport.Grpc.Linking;

namespace Schemata.Insight.Grpc;

public sealed class InsightLinkTargetResolver : IGrpcLinkTargetResolver
{
    public GrpcLinkTarget? Resolve(SemanticTarget target) => target is DomainOperationTarget operation
        && operation.Operation == InsightConstants.Operations.Insight
        ? new(InsightGrpcMethods.Query.FullName, operation.Parameters ?? new Dictionary<string, string?>()) : null;
    public SemanticTarget? GetTarget(string method, object request) => method == InsightGrpcMethods.Query.FullName
        ? new DomainOperationTarget(InsightConstants.Operations.Insight) : null;
}
