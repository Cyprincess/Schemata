using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Linking;
using Schemata.Common.Linking;
using Schemata.Core.Building;
using Schemata.Core.Linking;

namespace Schemata.Transport.Grpc.Linking;

/// <summary>
///     Collects keyed semantic contributors and writes Schemata's gRPC link projection to trailers.
/// </summary>
public sealed class GrpcLinkMetadataInterceptor : Interceptor
{
    /// <summary>
    ///     The trailer field carrying the serialized links.
    /// </summary>
    public const string TrailerName = "link";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest                               request,
        ServerCallContext                      context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        var response = await continuation(request, context);

        var http     = context.GetHttpContext();
        var resource = http.GetEndpoint()?.Metadata.GetMetadata<ResourceRegistration>();
        var projector = http.RequestServices.GetRequiredService<GrpcLinkProjector>();
        var state = projector.CreateContext(context.Method, request, response, resource);

        var links = new List<WebLink>();
        foreach (var contributor in http.RequestServices.GetKeyedServices<ILinkContributor>(LinkTransports.Grpc)) {
            foreach (var link in contributor.GetLinks(state)) {
                if (projector.Project(link, state) is { } projected) links.Add(projected);
            }
        }

        if (links.Count == 0) {
            return response;
        }

        var value = LinkValue.Serialize(links);

        if (value.Length > 0) {
            context.ResponseTrailers.Add(TrailerName, value);
        }

        return response;
    }
}
