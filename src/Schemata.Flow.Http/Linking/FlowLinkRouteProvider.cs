using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Schemata.Flow.Http.Controllers;
using Schemata.Flow.Skeleton;
using Schemata.Transport.Http.Linking;

namespace Schemata.Flow.Http.Linking;

public sealed class FlowLinkRouteProvider(IEnumerable<EndpointDataSource> sources) : ILinkRouteProvider
{
    public string? GetRoute(string operation) => operation == FlowConstants.LinkOperations.Definitions && IsPublished()
        ? FlowConstants.Routes.Definitions : null;

    public string? GetOperation(HttpRequest request) => HttpMethods.IsGet(request.Method)
        && request.Path == FlowConstants.Routes.Definitions && IsPublished() ? FlowConstants.LinkOperations.Definitions : null;

    private bool IsPublished() => sources.SelectMany(source => source.Endpoints).Any(endpoint =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
        && action.ControllerTypeInfo.AsType() == typeof(ProcessDefinitionsController)
        && action.MethodInfo.Name == nameof(ProcessDefinitionsController.ListProcessDefinitions)
        && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get, StringComparer.Ordinal) == true);
}
