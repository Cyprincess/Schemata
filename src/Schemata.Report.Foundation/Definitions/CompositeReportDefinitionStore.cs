using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation.Definitions;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Composes ordered report-definition sources with configuration definitions taking precedence.</summary>
/// <remarks>
///     Every <see cref="IReportDefinitionSource" /> is a singleton that scopes internally, so the composite
///     resolves them once at construction.
/// </remarks>
public sealed class CompositeReportDefinitionStore(IServiceProvider services) : IReportDefinitionStore
{
    private readonly IReadOnlyList<IReportDefinitionSource> _sources = [.. services.GetServices<IReportDefinitionSource>()];

    /// <inheritdoc />
    public async ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(
        string            name,
        CancellationToken ct = default
    ) {
        foreach (var source in _sources) {
            var definition = await source.ResolveAsync(name, ct);
            if (definition is not null) {
                return definition;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync(
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in _sources) {
            await foreach (var report in source.ListPeriodicAsync(ct)) {
                var name = report.Name ?? report.CanonicalName;
                if (name is not null && seen.Add(name)) {
                    yield return report;
                }
            }
        }
    }
}
