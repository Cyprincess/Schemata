using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Report.Foundation.Advisors;

/// <summary>
///     Rejects create and update requests on a Report capability entity while conflicting Report
///     entity triples are registered, before the pipeline maps or persists anything.
/// </summary>
/// <typeparam name="TEntity">The Report entity the request addresses.</typeparam>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ReportEntityCrudRequestAdvisor<TEntity, TRequest, TDetail>(ReportRegistration registration)
    : IResourceCreateRequestAdvisor<TEntity, TRequest, TDetail>,
      IResourceUpdateRequestAdvisor<TEntity, TRequest, TDetail>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly ReportRegistration _registration = registration;

    public int Order => ReportConstants.AdvisorOrders.CapabilityGuard;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        HandledResponse<CreateResultBase<TDetail>> handled,
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        HandledResponse<UpdateResultBase<TDetail>> handled,
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }
}
