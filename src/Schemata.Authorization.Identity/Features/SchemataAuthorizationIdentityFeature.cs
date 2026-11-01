using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Authorization.Identity.Features;

/// <summary>
///     Wires Schemata's Identity-backed subject provider and the subject-claims advisor into the
///     Authorization pipeline.
/// </summary>
[DependsOn(typeof(SchemataAuthorizationFeature<,,,,,>))]
[DependsOn<SchemataIdentityFeature>]
public sealed class SchemataAuthorizationIdentityFeature<TUser> : FeatureBase where TUser : SchemataUser
{
    /// <summary>Default feature priority for Identity-backed authorization integration.</summary>
    public const int DefaultPriority = SchemataAuthorizationOrders.Feature + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataIdentitySubjectProvider<TUser>();
}
