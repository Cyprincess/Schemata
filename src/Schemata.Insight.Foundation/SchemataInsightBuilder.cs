using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Fluent builder for the Insight module: enabled expression languages, the default language, the
///     total-size mode, registered sources, and source drivers.
/// </summary>
public sealed class SchemataInsightBuilder : IExpressionLanguageBuilder
{
    /// <summary>Creates the builder and binds the enabled languages to the module options.</summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="services">The service collection.</param>
    public SchemataInsightBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;

        Services.Configure<SchemataInsightOptions>(o => {
            if (Languages.Languages.Count > 0) {
                o.DefaultLanguage = Languages.Languages[0].Language;
            }
        });
    }

    private SchemataOptions Schemata { get; }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public ExpressionLanguageProfile Languages { get; } = new();

    /// <summary>Adds a feature to the Schemata configuration.</summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }

    /// <summary>Overrides the default expression language for value and predicate slots.</summary>
    /// <param name="language">The language identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder DefaultLanguage(string language) {
        Services.Configure<SchemataInsightOptions>(o => o.DefaultLanguage = language);
        return this;
    }

    /// <summary>Sets the <c>total_size</c> computation mode.</summary>
    /// <param name="mode">The total-size mode.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder WithTotalSize(TotalSizeMode mode) {
        Services.Configure<SchemataInsightOptions>(o => o.TotalSize = mode);
        return this;
    }

    /// <summary>
    ///     Resolves source names through the database catalog (over
    ///     <c>IRepository&lt;SchemataInsightSource&gt;</c>) before the in-memory sources. The host must
    ///     register that repository.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder UseDatabaseCatalog() {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IInsightSourceCatalog, DatabaseInsightSourceCatalog>());
        return this;
    }

    /// <summary>Registers a source name resolved by a driver with driver-specific parameters.</summary>
    /// <param name="name">The caller-facing source name.</param>
    /// <param name="driver">The driver name that serves this source.</param>
    /// <param name="parameters">The driver-specific parameters.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder AddSource(
        string                                name,
        string                                driver,
        IReadOnlyDictionary<string, object?>? parameters = null
    ) {
        var config = new SourceConfig(driver, parameters ?? new Dictionary<string, object?>());
        Services.Configure<SchemataInsightOptions>(o => o.Sources[name] = config);
        return this;
    }

    /// <summary>Registers a repository-backed source over a resource collection.</summary>
    /// <param name="name">The caller-facing source name.</param>
    /// <param name="resource">The resource collection the repository driver reads.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder AddRepositorySource(string name, string resource) {
        return AddSource(name, RepositoryDriver.DriverName, new Dictionary<string, object?> { ["resource"] = resource });
    }

    /// <summary>Registers a source driver under its keyed name.</summary>
    /// <typeparam name="TDriver">The driver type.</typeparam>
    /// <param name="name">The driver name used in <see cref="SourceConfig.DriverName" />.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder AddSourceDriver<TDriver>(string name)
        where TDriver : class, ISourceDriver {
        Services.AddKeyedSingleton<ISourceDriver, TDriver>(name);
        return this;
    }
}
