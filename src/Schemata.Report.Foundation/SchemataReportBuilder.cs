using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Report.Foundation;

/// <summary>Fluent builder for Report features and options.</summary>
public sealed partial class SchemataReportBuilder<TReport, TSnapshot, TChunk>
{
    private readonly HashSet<string> _definitionNames = new(System.StringComparer.Ordinal);

    /// <summary>Creates the Report builder.</summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="services">The service collection.</param>
    public SchemataReportBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    private SchemataOptions Schemata { get; }

    /// <summary>The service collection receiving Report registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Adds a feature to the Schemata configuration.</summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }
}
