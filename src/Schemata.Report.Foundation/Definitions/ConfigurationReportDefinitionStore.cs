using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Common;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Resolves configuration and DSL report definitions from the immutable options snapshot.</summary>
public sealed class ConfigurationReportDefinitionStore : Definitions.IReportDefinitionSource
{
    private readonly SchemataReportOptions _options;
    private readonly IServiceScopeFactory  _scopes;

    /// <summary>Creates a configuration definition store.</summary>
    /// <param name="scopes">Scope factory used for keyed program-provider resolution.</param>
    /// <param name="options">Configuration snapshot containing DSL registrations.</param>
    public ConfigurationReportDefinitionStore(
        IServiceScopeFactory                 scopes,
        IOptions<SchemataReportOptions> options
    ) {
        _scopes   = scopes;
        _options  = options.Value;
    }

    /// <inheritdoc />
    public async ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(
        string            name,
        CancellationToken ct
    ) {
        var registration = Find(name);
        if (registration is null) {
            return null;
        }

        var report = ToReport(registration);
        if (registration.SourceKind is ReportSourceKind.Expression) {
            return registration.Query is { } query
                ? (report, query)
                : throw new InvalidOperationException($"Report '{name}' has no expression definition.");
        }

        await using var scope = _scopes.CreateAsyncScope();
        var key = registration.Provider ?? registration.Name;
        var provider = scope.ServiceProvider.GetKeyedService<IReportDefinitionProvider>(key)
                       ?? throw new InvalidOperationException($"Report provider '{key}' is not registered.");
        return (report, await provider.GetDefinitionAsync(ct));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync(
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var registration in _options.Definitions) {
            ct.ThrowIfCancellationRequested();
            if (registration.Periodic) {
                yield return ToReport(registration);
            }

            await Task.CompletedTask;
        }
    }

    private ReportDefinitionRegistration? Find(string name) {
        foreach (var registration in _options.Definitions) {
            if (string.Equals(registration.Name, name, StringComparison.Ordinal)) {
                return registration;
            }
        }

        return null;
    }

    private static SchemataReport ToReport(ReportDefinitionRegistration registration) {
        return new() {
            Name           = registration.Name,
            CanonicalName  = $"reports/{registration.Name}",
            SourceKind     = registration.SourceKind,
            Provider       = registration.Provider,
            Periodic       = registration.Periodic,
            ScheduleKind   = registration.ScheduleKind,
            CronExpression = registration.CronExpression,
            IntervalTicks  = registration.IntervalTicks,
            Retention      = registration.Retention,
            Definition = registration.Query is null
                ? null
                : JsonSerializer.Serialize(registration.Query, SchemataJson.Default),
        };
    }
}
