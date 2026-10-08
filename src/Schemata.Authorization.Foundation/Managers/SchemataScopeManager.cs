using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="IScopeManager{TScope}" /> backed by an
///     <see cref="IRepository{TEntity}" /> for reads and the open-generic
///     <see cref="IResourceMutation{TScope}" /> owner for writes.
/// </summary>
/// <typeparam name="TScope">The scope entity type, must derive from <see cref="SchemataScope" />.</typeparam>
/// <seealso cref="SchemataApplicationManager{TApplication,TAuthorization}" />
public class SchemataScopeManager<TScope> : IScopeManager<TScope>
    where TScope : SchemataScope
{
    private readonly IServiceProvider _services;
    private readonly IResourceMutation<TScope> _mutation;

    public SchemataScopeManager(IServiceProvider services, IResourceMutation<TScope> mutation) {
        _services = services;
        _mutation = mutation;
    }

    #region IScopeManager<TScope> Members

    public async Task<TScope?> FindByNameAsync(string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        await using var scopes = _services.GetRequiredService<IRepository<TScope>>();
        return await scopes.SingleOrDefaultAsync(q => q.Where(s => s.Name == name), ct);
    }

    public async IAsyncEnumerable<TScope> ListAsync(IEnumerable<string>? names = null,
        [EnumeratorCancellation] CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var scopes = _services.GetRequiredService<IRepository<TScope>>();
        await foreach (var scope in scopes.ListAsync<TScope>(names is null ? null
                           : q => q.Where(s => names.Contains(s.Name)), ct)) {
            yield return scope;
        }
    }

    public async Task<TScope?> CreateAsync(TScope? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (scope is null) {
            return null;
        }

        await _mutation.CreateAsync(scope, null, ct);

        return scope;
    }

    public async Task UpdateAsync(TScope? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (scope is null) {
            return;
        }

        await _mutation.UpdateAsync(scope, null, Operations.Update, ct);
    }

    public async Task DeleteAsync(TScope? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (scope is null) {
            return;
        }

        await _mutation.DeleteAsync(scope, null, Operations.Delete, ct);
    }

    public Task SetDisplayNameAsync(TScope? scope, string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        scope?.DisplayName = name;

        return Task.CompletedTask;
    }

    public Task SetDisplayNamesAsync(TScope? scope, Dictionary<string, string?>? names, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        scope?.DisplayNames = names;

        return Task.CompletedTask;
    }

    public Task SetDescriptionAsync(TScope? scope, string? description, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        scope?.Description = description;

        return Task.CompletedTask;
    }

    public Task SetDescriptionsAsync(
        TScope?                      scope,
        Dictionary<string, string?>? descriptions,
        CancellationToken            ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        scope?.Descriptions = descriptions;

        return Task.CompletedTask;
    }

    public Task SetResourcesAsync(TScope? scope, ICollection<string>? resources, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        scope?.Resources = resources;

        return Task.CompletedTask;
    }

    #endregion
}
