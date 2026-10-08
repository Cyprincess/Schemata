using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Core.Features;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation.Catalog;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Security.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Fluent builder for the Insight module: enabled expression languages, the default language, the
///     total-size mode, registered sources, and source drivers.
/// </summary>
public sealed class SchemataInsightBuilder : IExpressionLanguageBuilder, IResourceBuilder
{
    internal const string AuthenticationSchemeKey = "Insight:AuthenticationScheme";

    /// <summary>Creates the builder and binds the enabled languages to the module options.</summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="services">The service collection.</param>
    public SchemataInsightBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            _ => { },
            _ => throw new InvalidOperationException("Insight authorization is configured through InsightSecurityGate per-source authorization."),
            scheme => Schemata.Set(AuthenticationSchemeKey, scheme));

        Services.Configure<SchemataInsightOptions>(o => {
            if (Languages.Languages.Count > 0) {
                o.DefaultLanguage = Languages.Languages[0].Language;
            }
        });
    }

    public SchemataOptions Schemata { get; }

    public IServiceCollection Services { get; }

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

    /// <summary>
    ///     Registers a repository-backed source with a closed projection. The projection binds the
    ///     entity's row type to the public shape so the driver can lower against a closed generic
    ///     surface and the row-level entitlement stays scoped to the entity query.
    /// </summary>
    /// <typeparam name="TEntity">The repository's row type.</typeparam>
    /// <typeparam name="TPublic">The shape materialized into rows.</typeparam>
    /// <param name="name">The caller-facing source name.</param>
    /// <param name="projection">The projection from entity to public shape.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataInsightBuilder AddRepositorySource<TEntity, TPublic>(
        string                          name,
        Expression<Func<TEntity, TPublic>> projection
    )
        where TEntity : class
        where TPublic : class {
        Services.AddKeyedSingleton<RepositorySource>(name, new RepositorySource<TEntity, TPublic>(projection));
        return AddSource(name, RepositoryDriver.DriverName, new Dictionary<string, object?> { ["binding"] = name });
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
