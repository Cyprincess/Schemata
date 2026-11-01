using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Grpc.Mapping;
using Schemata.Insight.Grpc.Wire;
using Schemata.Messaging.Skeleton;

using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Queries;
namespace Schemata.Insight.Grpc;

/// <summary>
///     Maps gRPC messages over the registered query handler. The shared exception interceptor
///     consumes the canonical Schemata exception produced by the domain.
/// </summary>
public sealed class InsightGrpcService : IInsightGrpcService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IServiceProvider     _services;

    /// <summary>Wires the gRPC service over the core query handler, resolving the caller principal via the HTTP context.</summary>
    /// <param name="services">The scoped provider resolving the query handler.</param>
    /// <param name="accessor">The HTTP context accessor for the caller principal.</param>
    public InsightGrpcService(IServiceProvider services, IHttpContextAccessor accessor) {
        _services = services;
        _accessor = accessor;
    }

    #region IInsightGrpcService Members

    public async ValueTask<QueryInsightGrpcResponse> QueryAsync(
        QueryInsightGrpcRequest request,
        CallContext             context = default
    ) {
        var query     = InsightStructMapper.ToRequest(request);
        var principal = _accessor.HttpContext?.User;
        query.Principal = principal;

        var dispatcher = _services.GetRequiredService<IRequestDispatcher>();
        var response = await dispatcher.SendAsync<QueryInsightRequest, QueryInsightResponse>(query, context.CancellationToken);

        return InsightStructMapper.ToResponse(response);
    }

    #endregion

}
