using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Report.Foundation.Dsl;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation;

public sealed partial class SchemataReportBuilder<TReport, TSnapshot, TChunk>
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <summary>Defines a program-backed report through the fluent report-definition DSL.</summary>
    /// <param name="name">Unique report leaf name within this builder.</param>
    /// <param name="configure">Configures the report query, schedule, and retention policy.</param>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty, whitespace, or already defined.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public void Define(string name, Action<ReportDefinitionBuilder> configure) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Report name must not be empty or whitespace.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(configure);
        if (!_definitionNames.Add(name)) {
            throw new ArgumentException($"A report named '{name}' is already defined.", nameof(name));
        }

        try {
            var definition = new ReportDefinitionBuilder();
            configure(definition);
            var registration = definition.ToRegistration(name);

            ServiceCollectionServiceExtensions.AddKeyedSingleton<IReportDefinitionProvider>(Services, name,
                                                                                            (_, _) => new ProgramReportDefinitionProvider(definition)
            );
            OptionsServiceCollectionExtensions.Configure<SchemataReportOptions>(Services, options => options.Definitions.Add(registration));
        } catch {
            _definitionNames.Remove(name);
            throw;
        }
    }

    /// <summary>
    ///     Selects a custom <see cref="IReportService" /> implementation for this capability. The
    ///     framework-owned facade stays the registered <see cref="IReportService" /> — it enforces
    ///     the single-triple guard before constructing the selected implementation.
    ///     Applications replacing the public interface directly own its full execution contract.
    /// </summary>
    /// <typeparam name="TService">The custom implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public SchemataReportBuilder<TReport, TSnapshot, TChunk> UseService<TService>()
        where TService : class, IReportService {
        Services.TryAddScoped<TService>();
        Services.AddKeyedScoped<IReportService>(ReportConstants.Services.Selected, (sp, _) => {
            sp.GetRequiredService<ReportRegistration>().EnsureSingleTriple<TReport>();
            return sp.GetRequiredService<TService>();
        });
        return this;
    }

    /// <summary>
    ///     Selects a custom <see cref="IReportSnapshotStore" /> implementation for this capability.
    ///     The framework-owned facade stays the registered <see cref="IReportSnapshotStore" /> — it
    ///     enforces the single-triple guard before any snapshot I/O, then delegates to the selected
    ///     implementation. Applications replacing the public interface directly own its full contract.
    /// </summary>
    /// <typeparam name="TStore">The custom implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public SchemataReportBuilder<TReport, TSnapshot, TChunk> UseSnapshotStore<TStore>()
        where TStore : class, IReportSnapshotStore {
        Services.TryAddScoped<TStore>();
        Services.AddKeyedScoped<IReportSnapshotStore>(ReportConstants.Services.Selected, (sp, _) => {
            sp.GetRequiredService<ReportRegistration>().EnsureSingleTriple<TSnapshot>();
            return sp.GetRequiredService<TStore>();
        });
        return this;
    }

}
