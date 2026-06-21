using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Insight.Grpc;

/// <summary>
///     Delegates the gRPC query to <see cref="IInsightService" />, mapping the edge messages to and
///     from the core wire types and translating Insight rejections into <see cref="SchemataException" />
///     so the shared gRPC exception interceptor produces the right status.
/// </summary>
public sealed class InsightGrpcService : IInsightGrpcService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IInsightService      _service;

    /// <summary>Wires the gRPC service over the core query service, resolving the caller principal via the HTTP context.</summary>
    /// <param name="service">The core query service.</param>
    /// <param name="accessor">The HTTP context accessor for the caller principal.</param>
    public InsightGrpcService(IInsightService service, IHttpContextAccessor accessor) {
        _service  = service;
        _accessor = accessor;
    }

    #region IInsightGrpcService Members

    public async ValueTask<QueryInsightGrpcResponse> QueryAsync(
        QueryInsightGrpcRequest request,
        CallContext             context = default
    ) {
        var query     = InsightStructMapper.ToRequest(request);
        var principal = _accessor.HttpContext?.User;

        QueryInsightResponse response;
        try {
            response = await _service.QueryAsync(query, principal, context.CancellationToken);
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

        return new SchemataException(code, status, ex.Message);
    }
}
