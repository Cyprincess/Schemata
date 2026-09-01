using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Core.Features;
using Schemata.Security.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation;

/// <summary>Fluent builder for Report features and options.</summary>
public sealed partial class SchemataReportBuilder<TReport, TSnapshot, TChunk> : IResourceBuilder
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    internal const string AuthenticationSchemeKey = "Report:AuthenticationScheme";

    private readonly HashSet<string> _definitionNames = new(System.StringComparer.Ordinal);

    /// <summary>Creates the Report builder.</summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="services">The service collection.</param>
    public SchemataReportBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            services => services.AddReportAuthentication<TReport, TSnapshot, TChunk>(),
            services => services.AddReportAuthorization<TReport, TSnapshot, TChunk>(),
            scheme => Schemata.Set(AuthenticationSchemeKey, scheme));
    }

    public SchemataOptions Schemata { get; }

    /// <summary>The service collection receiving Report registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Adds a feature to the Schemata configuration.</summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }

}
