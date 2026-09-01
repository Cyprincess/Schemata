using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Features;
using Schemata.Security.Skeleton;

namespace Schemata.Authorization.Grpc.Features;

[DependsOn(typeof(SchemataAuthorizationFeature<,,,>))]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataAuthorizationGrpcFeature<TApp, TAuth, TScope, TToken> : FeatureBase
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
    where TToken : SchemataToken, new()
{
    public const int DefaultPriority = SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>.DefaultPriority + 200_000;

    public override int Priority => DefaultPriority;
    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) {
        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken>.AuthenticationSchemeKey),
        };
        resources.Use<TApp, ApplicationRequest, ApplicationDetail, ApplicationSummary>([GrpcResourceAttribute.Name]);
        resources.Use<TScope, ScopeRequest, ScopeDetail, ScopeSummary>([GrpcResourceAttribute.Name]);
        resources.Use<TToken, TToken, TToken, TToken>([GrpcResourceAttribute.Name]);
    }
}
