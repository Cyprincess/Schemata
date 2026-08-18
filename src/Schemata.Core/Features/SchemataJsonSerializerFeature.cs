using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Configures <see cref="JsonSerializerOptions" /> with snake_case naming, string-number coercion,
///     kebab-case enums, and polymorphic type resolution. MVC JSON options are configured only when
///     controllers are present.
/// </summary>
public sealed class SchemataJsonSerializerFeature : FeatureBase
{
    /// <summary>
    ///     Default service-configuration priority for JSON serializer setup.
    /// </summary>
    public const int DefaultPriority = SchemataControllersFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataJsonSerializer(
        configurators.PopOrDefault<JsonSerializerOptions>(),
        schemata.HasFeature<SchemataControllersFeature>());
}
