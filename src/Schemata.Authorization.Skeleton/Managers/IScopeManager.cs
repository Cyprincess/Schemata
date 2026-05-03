using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

/// <summary>
///     Manages <see cref="SchemataScope" /> entities.
/// </summary>
public interface IScopeManager<TScope>
    where TScope : SchemataScope
{
    /// <summary>Lists scopes, optionally filtered by name.</summary>
    IAsyncEnumerable<TScope> ListAsync(IEnumerable<string>? names = null, CancellationToken ct = default);

    /// <summary>Finds a scope by its name.</summary>
    Task<TScope?> FindByNameAsync(string? name, CancellationToken ct = default);

    /// <summary>Creates a new scope.</summary>
    Task<TScope?> CreateAsync(TScope? scope, CancellationToken ct = default);

    /// <summary>Updates an existing scope.</summary>
    Task UpdateAsync(TScope? scope, CancellationToken ct = default);

    /// <summary>Deletes a scope.</summary>
    Task DeleteAsync(TScope? scope, CancellationToken ct = default);

    /// <summary>Sets the display name for the scope.</summary>
    Task SetDisplayNameAsync(TScope? scope, string? name, CancellationToken ct = default);

    /// <summary>Sets localized display names for the scope.</summary>
    Task SetDisplayNamesAsync(TScope? scope, Dictionary<string, string>? names, CancellationToken ct = default);

    /// <summary>Sets the description for the scope.</summary>
    Task SetDescriptionAsync(TScope? scope, string? description, CancellationToken ct = default);

    /// <summary>Sets localized descriptions for the scope.</summary>
    Task SetDescriptionsAsync(TScope? scope, Dictionary<string, string>? descriptions, CancellationToken ct = default);

    /// <summary>Sets the API resources this scope grants access to.</summary>
    Task SetResourcesAsync(TScope? scope, ICollection<string>? resources, CancellationToken ct = default);
}
