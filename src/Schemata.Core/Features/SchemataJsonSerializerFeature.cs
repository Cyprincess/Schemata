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
///     Configures <see cref="JsonSerializerOptions" /> with snake_case naming,
///     string-number coercion, kebab-case enums, and polymorphic type resolution.
///     Also wires <see cref="JsonOptions" /> and
///     <see cref="Microsoft.AspNetCore.Mvc.JsonOptions" /> when controllers are
///     present.
/// </summary>
public sealed class SchemataJsonSerializerFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataControllersFeature.DefaultPriority + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
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
