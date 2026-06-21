using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     A source catalog backed by <see cref="IRepository{TEntity}" /> over
///     <see cref="SchemataInsightSource" />, so sources can be added, changed, and removed at runtime.
///     The repository is resolved per call from a fresh scope because the catalog is a singleton.
/// </summary>
public sealed class DatabaseInsightSourceCatalog : IInsightSourceCatalog
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Initializes the catalog backed by a per-call repository scope resolved from the supplied factory.</summary>
    /// <param name="scopes">The scope factory resolving the source repository per call.</param>
    public DatabaseInsightSourceCatalog(IServiceScopeFactory scopes) {
        _scopes = scopes;
    }

    #region IInsightSourceCatalog Members

    public async ValueTask<SourceConfig?> ResolveAsync(string name, CancellationToken ct) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataInsightSource>>();

        var record = await repository
                          .FirstOrDefaultAsync<SchemataInsightSource>(q => q.Where(s => s.Name == name), ct)
                          .ConfigureAwait(false);

        return record?.Driver is { } driver
            ? new SourceConfig(driver, ParseParams(record.Params))
            : null;
    }

    public async ValueTask<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataInsightSource>>();

        var names = new List<string>();
        await foreach (var record in repository.ListAsync<SchemataInsightSource>(null, ct).ConfigureAwait(false)) {
            if (record.Name is { } name) {
                names.Add(name);
            }
        }

        return names;
    }

    #endregion

    private static IReadOnlyDictionary<string, object?> ParseParams(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return new Dictionary<string, object?>();
        }

        var elements = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                    ?? new Dictionary<string, JsonElement>();
        var result = new Dictionary<string, object?>(elements.Count);
        foreach (var (key, element) in elements) {
            result[key] = ToValue(element);
        }

        return result;
    }

    private static object? ToValue(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var value) ? value : element.GetDouble(),
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.Null   => null,
            var _                => element.GetRawText(),
        };
    }
}
