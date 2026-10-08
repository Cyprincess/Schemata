using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.Building;
using Schemata.Resource.Http.Features;

namespace Schemata.Authorization.Http.Features;

[DependsOn(typeof(SchemataAuthorizationFeature<,,>))]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataAuthorizationHttpFeature<TApp, TAuth, TScope> : FeatureBase
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
{
    public const int DefaultPriority = SchemataAuthorizationFeature<TApp, TAuth, TScope>.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;
    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) {
        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(SchemataAuthorizationBuilder<TApp, TAuth, TScope>.AuthenticationSchemeKey),
        };
        resources.Use<TApp, ApplicationRequest, ApplicationDetail, ApplicationSummary>([HttpResourceAttribute.Name]);
        resources.Use<TScope, ScopeRequest, ScopeDetail, ScopeSummary>([HttpResourceAttribute.Name]);
        resources.Use<SchemataToken, SchemataToken, SchemataToken, SchemataToken>([HttpResourceAttribute.Name]);
    }
}
