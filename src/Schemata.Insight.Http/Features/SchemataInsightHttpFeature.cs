using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Insight.Foundation.Features;
using Schemata.Insight.Foundation;
using Schemata.Insight.Http.Linking;
using Schemata.Insight.Skeleton.Entities;
using Schemata.Transport.Http.Features;

namespace Schemata.Insight.Http.Features;

/// <summary>
///     Exposes the Insight query endpoint over HTTP by registering this assembly's
///     <see cref="InsightController" /> as an MVC application part for the host-selected source
///     entity. Error translation and JSON wire-name rewrites come from
///     <see cref="SchemataTransportHttpFeature" />.
/// </summary>
/// <typeparam name="TInsightSource">The persisted source entity type.</typeparam>
/// <remarks>
///     The type parameter carries the closed feature selection and ordering; the query endpoint is
///     entity-agnostic.
/// </remarks>
[DependsOn(typeof(SchemataInsightFeature<>))]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataInsightHttpFeature<TInsightSource> : FeatureBase
    where TInsightSource : SchemataInsightSource, new()
{
    /// <summary>The default endpoint priority for the Insight HTTP endpoint.</summary>
    public const int DefaultPriority = SchemataInsightFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataApplicationPart<InsightController>();
        services.AddSchemataLinkContributor<InsightLinkContributor>();

        var scheme = schemata.Get<string>(SchemataInsightBuilder.AuthenticationSchemeKey);
        if (!string.IsNullOrWhiteSpace(scheme)) {
            services.PostConfigure<MvcOptions>(options => options.Conventions.Add(new InsightControllerConvention(scheme)));
        }
    }
}
