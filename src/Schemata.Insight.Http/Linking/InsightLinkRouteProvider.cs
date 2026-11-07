using Microsoft.AspNetCore.Http;
using Schemata.Insight.Skeleton;
using Schemata.Transport.Http.Linking;

namespace Schemata.Insight.Http.Linking;

public sealed class InsightLinkRouteProvider : ILinkRouteProvider
{
    public string? GetRoute(string operation) => operation == InsightConstants.Operations.Insight ? "/v1/insight:query" : null;
    public string? GetOperation(HttpRequest request) => HttpMethods.IsPost(request.Method) && request.Path == "/v1/insight:query"
        ? InsightConstants.Operations.Insight : null;
}
