using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-164 handler that restores a soft-deleted resource.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
/// <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>
public sealed class UndeleteHandler<TEntity, TDetail>
    : IRequestHandler<UndeleteResourceRequest<TEntity, TDetail>, TDetail>
    where TEntity : class, ICanonicalName, ISoftDelete
    where TDetail : class, ICanonicalName
{
    private readonly ISimpleMapper        _mapper;
    private readonly IRepository<TEntity> _repository;

    /// <summary>
    ///     Initializes the built-in undelete handler.
    /// </summary>
    /// <param name="repository">The repository for the target resource.</param>
    /// <param name="mapper">The mapper that creates the detail response.</param>
    public UndeleteHandler(IRepository<TEntity> repository, ISimpleMapper mapper) {
        _repository = repository;
        _mapper     = mapper;
    }

    public async Task<TDetail> HandleAsync(
        UndeleteResourceRequest<TEntity, TDetail> request,
        CancellationToken                         ct = default
    ) {
        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(
                q => q.Where(e => e.CanonicalName == request.CanonicalName), ct);
        }

        if (entity is null) {
            throw SchemataResourceErrors.NotFound<TEntity>(request.CanonicalName);
        }

        if (entity.DeleteTime is null) {
            throw SchemataResourceErrors.AlreadyExists<TEntity>(
                request.CanonicalName,
                "Resource is not deleted.");
        }

        entity.DeleteTime = null;
        entity.PurgeTime  = null;

        await _repository.UpdateAsync(entity, ct);
        await _repository.CommitAsync(ct);

        var map = _mapper.Map<TEntity, TDetail>(entity);
        if (map is null) {
            throw new InvalidOperationException(
                $"Could not map '{typeof(TEntity).FullName}' to '{typeof(TDetail).FullName}'."
            );
        }

        return map;
    }
}
