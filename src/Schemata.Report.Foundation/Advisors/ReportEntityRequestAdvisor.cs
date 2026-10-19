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
///     Rejects get, list, and delete requests on a Report capability entity while conflicting
///     Report entity triples are registered, before the pipeline loads the addressed instance or
///     performs any business I/O.
/// </summary>
/// <typeparam name="TEntity">The Report or Snapshot entity the request addresses.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ReportEntityRequestAdvisor<TEntity, TDetail>(ReportRegistration registration)
    : IResourceGetRequestAdvisor<TEntity, TDetail>,
      IResourceListRequestAdvisor<TEntity, TDetail>,
      IResourceDeleteRequestAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly ReportRegistration _registration = registration;

    public int Order => ReportConstants.AdvisorOrders.CapabilityGuard;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        GetRequest                        request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        HandledResponse<GetResultBase<TDetail>> handled,
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        ListRequest                       request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        HandledResponse<ListResultBase<TEntity, TDetail>> handled,
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        DeleteRequest                     request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        HandledResponse<DeleteResultBase<TDetail>> handled,
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }
}
