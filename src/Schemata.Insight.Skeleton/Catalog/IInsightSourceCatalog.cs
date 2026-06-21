using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     Resolves a registered source name to its driver and parameters, hiding the backend, connection
///     parameters, and secrets from the caller.
/// </summary>
public interface IInsightSourceCatalog
{
    /// <summary>Resolves a source name, or null when unknown.</summary>
    ValueTask<SourceConfig?> ResolveAsync(string name, CancellationToken ct);

    /// <summary>Lists the registered source names.</summary>
    ValueTask<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct);
}
