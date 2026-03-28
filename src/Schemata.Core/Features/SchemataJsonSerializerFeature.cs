using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Errors;
using Schemata.Core.Json;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Core.Features;

/// <summary>
///     Configures JSON serialization with snake_case naming, polymorphic type resolution, and AIP conventions.
/// </summary>
public sealed class SchemataJsonSerializerFeature : FeatureBase
{
    public const int DefaultPriority = SchemataControllersFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.PopOrDefault<JsonSerializerOptions>();

        services.Configure<JsonSerializerOptions>(Configure);

        services.Configure<JsonOptions>(options => { Configure(options.SerializerOptions); });

        if (!schemata.HasFeature<SchemataControllersFeature>()) {
            return;
        }

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => {
            Configure(options.JsonSerializerOptions);
        });

        return;

        void Configure(JsonSerializerOptions options) {
            options.MaxDepth = 32;

            options.DictionaryKeyPolicy    = JsonNamingPolicy.SnakeCaseLower;
            options.PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower;
            options.NumberHandling         = JsonNumberHandling.AllowReadingFromString;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
            options.Converters.Add(JsonStringNumberConverter.Instance);

            options.TypeInfoResolver = PolymorphicTypeResolver.Instance.WithAddedModifier(info => {
                // Rename details type to "@type" per AIP conventions

                if (!typeof(IErrorDetail).IsAssignableFrom(info.Type)) {
                    return;
                }

                var property = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(IErrorDetail.Type),
                });

                property?.Name = Parameters.Type;
            });

            configure(options);
        }
    }
}
