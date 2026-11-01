using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Definitions;

/// <summary>Composes ordered report-definition sources with configuration definitions taking precedence.</summary>
/// <remarks>
///     Every <see cref="IReportDefinitionSource" /> is a singleton that scopes internally, so the composite
///     resolves them once at construction. Periodic scheduling follows the same precedence: a definition
///     shadowed for reads by a higher-precedence source is never scheduled.
/// </remarks>
public sealed class CompositeReportDefinitionStore(
    IServiceProvider     services,
    ReportRegistration   registration
) : IReportDefinitionStore
{
    private readonly IReadOnlyList<IReportDefinitionSource> _sources = [.. services.GetServices<IReportDefinitionSource>()];

    private readonly ReportRegistration _registration = registration;

    public async ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(
        string            name,
        CancellationToken ct = default
    ) {
        _registration.EnsureSingleTriple<SchemataReport>();

        foreach (var source in _sources) {
            var definition = await source.ResolveAsync(name, ct);
            if (definition is not null) {
                return definition;
            }
        }

        return null;
    }

    public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync(
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        _registration.EnsureSingleTriple<SchemataReport>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < _sources.Count; index++) {
            await foreach (var report in _sources[index].ListPeriodicAsync(ct)) {
                var name = report.Name ?? report.CanonicalName;
                if (name is null || !seen.Add(name)) {
                    continue;
                }

                if (await IsShadowedAsync(index, name, ct)) {
                    continue;
                }

                yield return report;
            }
        }
    }

    private async ValueTask<bool> IsShadowedAsync(int index, string name, CancellationToken ct) {
        for (var preceding = 0; preceding < index; preceding++) {
            if (await _sources[preceding].ResolveAsync(name, ct) is not null) {
                return true;
            }
        }

        return false;
    }
}
