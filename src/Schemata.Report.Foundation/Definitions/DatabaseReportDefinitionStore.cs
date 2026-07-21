using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Resolves persisted report definitions through a fresh repository scope per call.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
public sealed class DatabaseReportDefinitionStore<TReport> : Definitions.IReportDefinitionSource
    where TReport : SchemataReport
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates a persisted report-definition store.</summary>
    /// <param name="scopes">Scope factory used to resolve repositories and keyed providers per call.</param>
    public DatabaseReportDefinitionStore(IServiceScopeFactory scopes) { _scopes = scopes; }

    /// <inheritdoc />
    public async ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(
        string            name,
        CancellationToken ct
    ) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TReport>>();
        var report = await repository.FirstOrDefaultAsync(
                         query => query.Where(candidate => candidate.Name == name), ct);
        if (report is null) {
            return null;
        }

        return (report, await ResolveQueryAsync(scope.ServiceProvider, report, ct));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync(
        [EnumeratorCancellation] CancellationToken ct
    ) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TReport>>();
        await foreach (var report in repository.ListAsync(query => query.Where(candidate => candidate.Periodic), ct)) {
            yield return report;
        }
    }

    private static async ValueTask<QueryInsightRequest> ResolveQueryAsync(
        IServiceProvider serviceProvider,
        TReport          report,
        CancellationToken ct
    ) {
        if (report.SourceKind is ReportSourceKind.Program) {
            var provider = serviceProvider.GetKeyedService<IReportDefinitionProvider>(report.Provider)
                           ?? throw new InvalidOperationException($"Report provider '{report.Provider}' is not registered.");
            return await provider.GetDefinitionAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(report.Definition)) {
            throw new InvalidOperationException($"Report '{report.Name}' has no expression definition.");
        }

        return JsonSerializer.Deserialize<QueryInsightRequest>(report.Definition, SchemataJson.Default)
               ?? throw new InvalidOperationException($"Report '{report.Name}' has an invalid expression definition.");
    }
}
