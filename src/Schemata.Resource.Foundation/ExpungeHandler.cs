using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-164 handler that permanently removes a soft-deleted resource.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>
public sealed class ExpungeHandler<TEntity> : IResourceMethodHandler<TEntity, EmptyResourceRequest, EmptyResourceResponse>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private readonly IRepository<TEntity> _repository;

    /// <summary>
    ///     Initializes the built-in expunge handler.
    /// </summary>
    /// <param name="repository">The repository for the target resource.</param>
    public ExpungeHandler(IRepository<TEntity> repository) { _repository = repository; }

    public async ValueTask<EmptyResourceResponse> InvokeAsync(
        string?              name,
        EmptyResourceRequest request,
        TEntity?             entity,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.DeleteTime is null) {
            throw new FailedPreconditionException(message: $"Resource '{name ?? entity.CanonicalName ?? entity.Name}' is not deleted.");
        }

        using (_repository.SuppressSoftDelete()) {
            await _repository.RemoveAsync(entity, ct);
        }

        await _repository.CommitAsync(ct);

        // AIP-164 expunge carries no resource body; return an empty response.
        return new();
    }
}
