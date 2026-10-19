using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation.Advisors;

internal sealed class ReportCommandPipelineAdvisor<TEntity, TRequest, TResponse>(ReportRegistration registration)
    : IRequestPipelineAdvisor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public int Order => ReportConstants.AdvisorOrders.CapabilityGuard;

    public Task<TResponse> AdviseAsync(
        AdviceContext ctx,
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken ct) {
        registration.EnsureSingleTriple<TEntity>();
        return next(ct);
    }
}
