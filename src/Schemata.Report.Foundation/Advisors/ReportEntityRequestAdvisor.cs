using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Advisors;

/// <summary>
///     Rejects fixed-envelope standard resource operations (Get, List, Delete) on a Report
///     capability entity while conflicting Report entity triples are registered, before the
///     pipeline reaches repository I/O.
/// </summary>
/// <typeparam name="TEntity">The Report or Snapshot entity the operation addresses.</typeparam>
public sealed class ReportEntityRequestAdvisor<TEntity>(ReportRegistration registration)
    : IResourceGetRequestAdvisor<TEntity>,
      IResourceListRequestAdvisor<TEntity>,
      IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly ReportRegistration _registration = registration;

    public int Order => ReportConstants.AdvisorOrders.CapabilityGuard;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        GetRequest                        request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
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
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }
}