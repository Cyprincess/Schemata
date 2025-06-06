using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.Json;

namespace Schemata.Core.Features;

public sealed class SchemataJsonSerializerFeature : FeatureBase
{
    public override int Priority => 210_100_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.PopOrDefault<JsonSerializerOptions>();

        services.Configure<JsonSerializerOptions>(options => {
            options.MaxDepth = 32;

            Configure(options);
        });

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => {
            Configure(options.SerializerOptions);
        });

        if (!schemata.HasFeature<SchemataControllersFeature>()) {
            return;
        }

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => {
            Configure(options.JsonSerializerOptions);
        });

        return;

        void Configure(JsonSerializerOptions options) {
            options.DictionaryKeyPolicy  = JsonNamingPolicy.SnakeCaseLower;
            options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.NumberHandling       = JsonNumberHandling.AllowReadingFromString;

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
            options.Converters.Add(new JsonStringNumberConverter());

            options.TypeInfoResolver = new PolymorphicTypeResolver();

            configure(options);
        }
    }
}
