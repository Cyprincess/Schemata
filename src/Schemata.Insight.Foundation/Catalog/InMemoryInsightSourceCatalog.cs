using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     A source catalog backed by a dictionary loaded at builder time; suitable for sources known at
///     compile time.
/// </summary>
public sealed class InMemoryInsightSourceCatalog : IInsightSourceCatalog
{
    private readonly IReadOnlyDictionary<string, SourceConfig> _sources;

    /// <summary>Creates a catalog over the given source registrations.</summary>
    /// <param name="sources">The registered sources keyed by name.</param>
    public InMemoryInsightSourceCatalog(IReadOnlyDictionary<string, SourceConfig> sources) {
        _sources = sources;
    }

    /// <summary>Creates a catalog over the sources registered on the Insight options.</summary>
    /// <param name="options">The Insight options carrying the registered sources.</param>
    public InMemoryInsightSourceCatalog(IOptions<SchemataInsightOptions> options)
        : this(new Dictionary<string, SourceConfig>(options.Value.Sources)) { }

    #region IInsightSourceCatalog Members

    public ValueTask<SourceConfig?> ResolveAsync(string name, CancellationToken ct) {
        return new ValueTask<SourceConfig?>(_sources.GetValueOrDefault(name));
    }

    public ValueTask<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct) {
        return new ValueTask<IReadOnlyList<string>>(_sources.Keys.ToList());
    }

    #endregion
}
