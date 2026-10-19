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

namespace Schemata.Transport.Grpc.Linking;

/// <summary>
///     Collects every <see cref="IGrpcLinkContributor" /> link for a unary response and writes one
///     RFC 8288 link-value list into the <c>link</c> response trailer.
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
        var state    = new GrpcLinkContext(context, response, resource);

        var links = new List<WebLink>();
        foreach (var contributor in http.RequestServices.GetServices<IGrpcLinkContributor>()) {
            var contributed = contributor.GetLinks(state);
            if (contributed is null) {
                continue;
            }

            links.AddRange(contributed);
        }

        if (links.Count == 0) {
            return response;
        }

        string value;
        try {
            value = LinkValue.Serialize(links);
        } catch (ArgumentException) {
            // Links are ancillary response metadata; an invalid contributed target must not turn
            // an otherwise successful response into a failed call.
            return response;
        }

        if (value.Length > 0) {
            context.ResponseTrailers.Add(TrailerName, value);
        }

        return response;
    }
}
