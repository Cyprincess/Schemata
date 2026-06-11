using System;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Parlot;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-165 handler that dispatches purge as a long-running operation.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeHandler<TEntity> : IResourceMethodHandler<TEntity, PurgeRequest, PurgeResponse>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private const int SampleLimit = 100;

    private readonly IServiceProvider _services;

    /// <summary>
    ///     Initializes the built-in purge handler.
    /// </summary>
    /// <param name="services">Service provider used to resolve the dispatcher and compiler.</param>
    public PurgeHandler(IServiceProvider services) { _services = services; }

    public async ValueTask<PurgeResponse> InvokeAsync(
        string?          name,
        PurgeRequest     request,
        TEntity?         entity,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var filter = CompileFilter(request);
        var dispatcher = _services.GetService<IOperationDispatcher>()
                      ?? throw new InvalidOperationException(
                          "Purge requires an IOperationDispatcher; install Schemata.Scheduling.Http/Grpc.");

        var operation = await dispatcher.DispatchAsync(
            Verbs.Purge,
            async (sp, token) => await ExecuteAsync(sp, filter, request.Force, token),
            ct);

        return new() {
            Operation     = operation,
            CanonicalName = operation,
            Name          = operation.Split('/').LastOrDefault(),
        };
    }

    private Expression<Func<TEntity, bool>>? CompileFilter(PurgeRequest request) {
        if (string.IsNullOrWhiteSpace(request.Filter)) {
            throw InvalidFilter();
        }

        if (request.Filter == Wildcards.Any) {
            return null;
        }

        try {
            var compiler = _services.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
            var tree     = compiler.Parse(request.Filter);
            return compiler.Compile<TEntity, bool>(tree);
        } catch (Exception ex) when (ex is ParseException or ArgumentException) {
            throw InvalidFilter();
        }
    }

    private static async Task<PurgeResponse?> ExecuteAsync(
        IServiceProvider                 services,
        Expression<Func<TEntity, bool>>? filter,
        bool                             force,
        CancellationToken                ct
    ) {
        var repository = services.GetRequiredService<IRepository<TEntity>>();
        IQueryable<TEntity> Query(IQueryable<TEntity> q) {
            var eligible = q.Where(row => row.DeleteTime != null);
            return filter is null ? eligible : eligible.Where(filter);
        }

        var result = new PurgeResponse();
        using (repository.SuppressQuerySoftDelete()) {
            result.PurgeCount = await repository.LongCountAsync(Query, ct);
        }

        if (!force) {
            using (repository.SuppressQuerySoftDelete()) {
                await foreach (var row in repository.ListAsync(q => Query(q).Take(SampleLimit), ct)) {
                    var item = row.CanonicalName ?? row.Name;
                    if (!string.IsNullOrWhiteSpace(item)) {
                        result.PurgeSample.Add(item);
                    }
                }
            }

            return result;
        }

        using (repository.SuppressQuerySoftDelete()) {
            await foreach (var row in repository.ListAsync(Query, ct)) {
                using var removeSuppression = repository.SuppressSoftDelete();
                await repository.RemoveAsync(row, ct);
            }
        }

        await repository.CommitAsync(ct);

        return result;
    }

    private static ValidationException InvalidFilter() {
        return new([new() {
            Field       = SchemataNaming.ToWireName(nameof(PurgeRequest.Filter)),
            Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "filter"),
            Reason      = FieldReasons.InvalidFilter,
        }]);
    }
}
