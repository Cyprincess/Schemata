using Microsoft.AspNetCore.Http;
using Schemata.Push.Skeleton;
using Schemata.Transport.Http.Linking;

namespace Schemata.Push.Http.Linking;

public sealed class PushLinkRouteProvider : ILinkRouteProvider
{
    public string? GetRoute(string operation) => operation == PushConstants.Routes.Subscriptions ? PushConstants.Routes.SubscriptionsPath : null;
    public string? GetOperation(HttpRequest request) => HttpMethods.IsGet(request.Method) && request.Path == PushConstants.Routes.SubscriptionsPath
        ? PushConstants.Routes.Subscriptions : null;
}
