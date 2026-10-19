using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Abstractions.Advisors;

namespace Schemata.Resource.Foundation.Handlers;

/// <summary>
///     Built-in AIP-164 handler that permanently removes a soft-deleted resource.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>
public sealed class ExpungeHandler<TEntity> : IRequestHandler<ExpungeResourceRequest<TEntity>, EmptyResourceResponse>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private readonly IResourceMutation<TEntity> _mutation;
    private readonly IRepository<TEntity>      _repository;

    /// <summary>
    ///     Initializes the built-in expunge handler.
    /// </summary>
    /// <param name="repository">The repository for the target resource.</param>
    /// <param name="mutation">The resource mutation owner persisting the physical removal.</param>
    public ExpungeHandler(IRepository<TEntity> repository, IResourceMutation<TEntity> mutation) {
        _repository = repository;
        _mutation   = mutation;
    }

    public async Task<EmptyResourceResponse> HandleAsync(
        ExpungeResourceRequest<TEntity> request,
        AdviceContext                   context,
        CancellationToken               ct = default
    ) {
        var entity = await _repository.SingleOrDefaultAsync(
            q => q.Where(e => e.CanonicalName == request.CanonicalName), ct,
            new QueryOptions(IgnoreSoftDelete: true));

        if (entity is null) {
            throw SchemataResourceErrors.NotFound<TEntity>(request.CanonicalName);
        }

        if (entity.DeleteTime is null) {
            throw SchemataResourceErrors.PreconditionFailed<TEntity>(
                request.CanonicalName,
                PreconditionSubjects.StateNotDeleted,
                "Resource is not deleted.");
        }

        // The expunge intent passes IgnoreSoftDelete to the repository the mutation stages through.
        await _mutation.DeleteAsync(entity, null, Operations.Expunge, ct);

        return new();
    }
}
