using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Report.Foundation;

/// <summary>Fluent builder for Report features and options.</summary>
public sealed partial class SchemataReportBuilder<TReport, TSnapshot, TChunk>
{
    /// <summary>
    ///     Options-bag key carrying the authentication scheme set through
    ///     <see cref="WithAuthorization" />, read by the transport packages when they register the
    ///     Report resources.
    /// </summary>
    internal const string AuthenticationSchemeKey = "Report:AuthenticationScheme";

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

    /// <summary>
    ///     Requires <paramref name="scheme" /> on the Report resource endpoints, overriding the
    ///     resource system's global default for the Report resources alone. Call it before
    ///     <c>MapHttp()</c> / <c>MapGrpc()</c>, which read the scheme when they register the
    ///     resources.
    /// </summary>
    /// <param name="scheme">
    ///     The authentication scheme; <see langword="null" /> restores the global default.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public SchemataReportBuilder<TReport, TSnapshot, TChunk> WithAuthorization(string? scheme = null) {
        Schemata.Set(AuthenticationSchemeKey, scheme);

        return this;
    }
}
