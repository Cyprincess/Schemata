using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

public class SchemataJsonSerializerFeature : FeatureBase
{
    public override int Priority => 210_100_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.Configure<JsonSerializerOptions>(options => {
            options.MaxDepth             = 32;
            options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.NumberHandling       = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString;

            options.TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault
                ? new DefaultJsonTypeInfoResolver()
                : JsonTypeInfoResolver.Combine();
        });

        if (services.All(s => s.ServiceType != typeof(IActionDescriptorChangeProvider))) {
            return;
        }

        services.Configure<JsonOptions>(options => {
            var serializer = options.JsonSerializerOptions;
            serializer.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            serializer.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString;
        });
    }
}
