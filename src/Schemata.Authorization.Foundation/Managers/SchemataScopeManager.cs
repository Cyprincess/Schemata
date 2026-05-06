using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="IScopeManager{TScope}" /> backed by an
///     <see cref="IRepository{TEntity}" />.
/// </summary>
/// <typeparam name="TScope">The scope entity type, must derive from <see cref="SchemataScope" />.</typeparam>
/// <seealso cref="SchemataApplicationManager{TApplication, TScope}" />
public class SchemataScopeManager<TScope> : IScopeManager<TScope>
    where TScope : SchemataScope
{
    private readonly IRepository<TScope> _scopes;

    public SchemataScopeManager(IRepository<TScope> scopes) { _scopes = scopes; }

    #region IScopeManager<TScope> Members

    public async Task<TScope?> FindByNameAsync(string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return await _scopes.SingleOrDefaultAsync(q => q.Where(s => s.Name == name), ct);
    }

    public IAsyncEnumerable<TScope> ListAsync(IEnumerable<string>? names = null, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (names is null) {
            return _scopes.AsAsyncEnumerable();
        }

        return _scopes.ListAsync(q => q.Where(s => names.Contains(s.Name)), ct);
    }

    public async Task<TScope?> CreateAsync(TScope? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (scope is null) {
            return null;
        }

        await _scopes.AddAsync(scope, ct);
        await _scopes.CommitAsync(ct);

        return scope;
    }

    public async Task UpdateAsync(TScope? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (scope is null) {
            return;
        }

        await _scopes.UpdateAsync(scope, ct);
        await _scopes.CommitAsync(ct);
    }

    public async Task DeleteAsync(TScope? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (scope is null) {
            return;
        }

        await _scopes.RemoveAsync(scope, ct);
        await _scopes.CommitAsync(ct);
    }

    public Task SetDisplayNameAsync(TScope? scope, string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        scope?.DisplayName = name;

        return Task.CompletedTask;
    }

    public Task SetDisplayNamesAsync(TScope? scope, Dictionary<string, string>? names, CancellationToken ct = default) {
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
        TScope?                     scope,
        Dictionary<string, string>? descriptions,
        CancellationToken           ct = default
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
