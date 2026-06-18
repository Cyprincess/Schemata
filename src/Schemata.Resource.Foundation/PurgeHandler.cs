using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-165 handler that dispatches purge as a long-running operation.
///     The work runs in <see cref="PurgeOperationHandler{TEntity}" />, rebuilt from the
///     persisted request data so it survives a host restart.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeHandler<TEntity> : IResourceMethodHandler<TEntity, PurgeRequest, Operation>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private readonly IServiceProvider _services;

    /// <summary>
    ///     Initializes the built-in purge handler.
    /// </summary>
    /// <param name="services">Service provider used to resolve the dispatcher and compiler.</param>
    public PurgeHandler(IServiceProvider services) { _services = services; }

    public async ValueTask<Operation> InvokeAsync(
        string?           name,
        PurgeRequest      request,
        TEntity?          entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        // Validate the filter up front so a malformed request fails fast with
        // INVALID_ARGUMENT instead of dispatching a doomed operation. The compiled
        // expression is discarded; the durable handler recompiles it from the
        // persisted filter string at execution time.
        _ = PurgeFilter.Compile<TEntity>(_services, request.Filter);

        var dispatcher = _services.GetService<IOperationDispatcher>()
                      ?? throw new InvalidOperationException("Purge requires an IOperationDispatcher; install Schemata.Scheduling.Http/Grpc.");

        var collection = ResourceNameDescriptor.ForType<TEntity>().Collection;
        var key        = $"{Verbs.Purge}:{collection}";

        return await dispatcher.DispatchAsync(
            key,
            new PurgeOperationArgs { Filter = request.Filter, Force = request.Force },
            ct);
    }
}
