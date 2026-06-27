using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-164 handler that restores a soft-deleted resource.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
/// <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>
public sealed class UndeleteHandler<TEntity, TDetail> : IResourceMethodHandler<TEntity, EmptyResourceRequest, TDetail>
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

    public async ValueTask<TDetail> InvokeAsync(
        string?              name,
        EmptyResourceRequest request,
        TEntity?             entity,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.DeleteTime is null) {
            throw SchemataResourceErrors.PreconditionFailed<TEntity>(
                name: name ?? entity.CanonicalName,
                subject: PreconditionSubjects.NotSoftDeleted,
                description: "Resource is not deleted.");
        }

        entity.DeleteTime = null;
        entity.PurgeTime  = null;

        await _repository.UpdateAsync(entity, ct);
        await _repository.CommitAsync(ct);

        return _mapper.Map<TEntity, TDetail>(entity)
            ?? throw new InvalidOperationException($"Could not map '{typeof(TEntity).FullName}' to '{typeof(TDetail).FullName}'.");
    }
}
