using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

public interface IScopeManager<TScope>
    where TScope : SchemataScope
{
    IAsyncEnumerable<TScope> ListAsync(IEnumerable<string>? names = null, CancellationToken ct = default);

    Task<TScope?> FindByNameAsync(string? name, CancellationToken ct = default);

    Task<TScope?> CreateAsync(TScope? scope, CancellationToken ct = default);

    Task UpdateAsync(TScope? scope, CancellationToken ct = default);

    Task DeleteAsync(TScope? scope, CancellationToken ct = default);

    Task SetDisplayNameAsync(TScope? scope, string? name, CancellationToken ct = default);

    Task SetDisplayNamesAsync(TScope? scope, Dictionary<string, string>? names, CancellationToken ct = default);

    Task SetDescriptionAsync(TScope? scope, string? description, CancellationToken ct = default);

    Task SetDescriptionsAsync(TScope? scope, Dictionary<string, string>? descriptions, CancellationToken ct = default);

    Task SetResourcesAsync(TScope? scope, ICollection<string>? resources, CancellationToken ct = default);
}
