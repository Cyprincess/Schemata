using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Insight.Grpc;

/// <summary>
///     Maps the gRPC edge messages to and from the core wire types, dispatches the query through the
///     registered handler, and translates Insight rejections into <see cref="SchemataException" />
///     so the shared gRPC exception interceptor produces the right status.
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

        QueryInsightResponse response;
        try {
            var dispatcher = _services.GetRequiredService<IRequestDispatcher>();
            response = await dispatcher.SendAsync<QueryInsightRequest, QueryInsightResponse>(query, context.CancellationToken);
        } catch (InsightValidationException ex) {
            throw Translate(ex);
        }

        return InsightStructMapper.ToResponse(response);
    }

    #endregion

    private static SchemataException Translate(InsightValidationException ex) {
        // The gRPC interceptor derives the status from the canonical google.rpc code, so map the
        // Insight reason to one; the specific reason stays in the message.
        var (code, status) = ex.Reason switch {
            InsightReasons.UnknownSourceName => (404, ErrorCodes.NotFound),
            var _                            => (400, ErrorCodes.InvalidArgument),
        };

        return new(code, status, ex.Message);
    }
}
