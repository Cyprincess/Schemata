using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Abstractions;
using Schemata.Abstractions.Resource;
using Schemata.Abstractions.Linking;
using Schemata.Common;

namespace Schemata.Transport.Grpc.Linking;

/// <summary>
///     Projects the AIP-158 <c>next_page_token</c> of a paginated gRPC response into a
///     <c>rel="next"</c> link whose target is the current RPC and whose <c>page_token</c> target
///     attribute carries the continuation.
/// </summary>
public sealed class ResourcePaginationLinkContributor : IGrpcLinkContributor
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> NextPageTokens = new();

    #region IGrpcLinkContributor Members

    public IEnumerable<WebLink> GetLinks(GrpcLinkContext context) {
        if (context.Resource is null || context.Response is null) {
            return [];
        }

        var property = NextPageTokens.GetOrAdd(context.Response.GetType(),
            static type => AppDomainTypeCache.GetProperty(type, nameof(ListResultBase<object, object>.NextPageToken)));
        if (property?.GetValue(context.Response) is not string { Length: > 0 } token) {
            return [];
        }

        return [
            new WebLink(context.Call.Method, "next", Extensions: new Dictionary<string, string>(StringComparer.Ordinal) {
                [SchemataConstants.Pagination.PageToken] = token,
            }),
        ];
    }

    #endregion
}
