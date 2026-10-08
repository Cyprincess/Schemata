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
///     Rejects custom resource methods (run, generate, snapshot :read) on a Report capability
///     entity while conflicting Report entity triples are registered, before the pipeline loads
///     the addressed instance or performs any business I/O.
/// </summary>
/// <typeparam name="TEntity">The Report or Snapshot entity the method addresses.</typeparam>
/// <typeparam name="TRequest">The custom method's request type.</typeparam>
public sealed class ReportEntityMethodRequestAdvisor<TEntity, TRequest>(ReportRegistration registration)
    : IResourceMethodRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class
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