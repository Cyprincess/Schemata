using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

public sealed partial class SchemataReportBuilder<TReport, TSnapshot, TChunk>
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

            Services.AddKeyedSingleton<IReportDefinitionProvider>(
                name,
                (_, _) => new ProgramReportDefinitionProvider(definition)
            );
            Services.Configure<SchemataReportOptions>(options => options.Definitions.Add(registration));
        } catch {
            _definitionNames.Remove(name);
            throw;
        }
    }

}
