using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Report.Foundation.Advisors;

/// <summary>
///     Rejects request-shaped standard resource operations (Create, Update) on a Report capability
///     entity while conflicting Report entity triples are registered, before the pipeline reaches
///     repository I/O.
/// </summary>
/// <typeparam name="TEntity">The Report or Snapshot entity the operation addresses.</typeparam>
/// <typeparam name="TRequest">The create/update request DTO type.</typeparam>
public sealed class ReportEntityCrudRequestAdvisor<TEntity, TRequest>(ReportRegistration registration)
    : IResourceCreateRequestAdvisor<TEntity, TRequest>,
      IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly ReportRegistration _registration = registration;

    public int Order => ReportConstants.AdvisorOrders.CapabilityGuard;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        _registration.EnsureSingleTriple<TEntity>();
        return Task.FromResult(AdviseResult.Continue);
    }
}