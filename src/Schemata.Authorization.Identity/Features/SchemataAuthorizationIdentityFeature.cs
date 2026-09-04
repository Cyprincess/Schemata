using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation.Features;

namespace Schemata.Authorization.Identity.Features;

/// <summary>
///     Wires Schemata's Identity-backed subject provider and the subject-claims advisor into the
///     Authorization pipeline.
/// </summary>
[DependsOn(typeof(SchemataAuthorizationFeature<,,>))]
[DependsOn(typeof(SchemataIdentityFeature<,,,>))]
public sealed class SchemataAuthorizationIdentityFeature : FeatureBase
{
    /// <summary>Default feature priority for Identity-backed authorization integration.</summary>
    public const int DefaultPriority = SchemataAuthorizationFeature<SchemataApplication, SchemataAuthorization, SchemataScope>.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataIdentitySubjectProvider();
}
