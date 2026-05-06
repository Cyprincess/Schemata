using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Mapping.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Mapping.Foundation.Features;

public static class SchemataMappingFeature
{
    public const int DefaultPriority = Orders.Extension + 30_000_000;
}

/// <summary>
///     Feature that registers a concrete <see cref="ISimpleMapper" /> implementation as a scoped service.
/// </summary>
/// <typeparam name="T">The mapper implementation type.</typeparam>
public sealed class SchemataMappingFeature<T> : FeatureBase
    where T : class, ISimpleMapper
{
    public override int Priority => SchemataMappingFeature.DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped<ISimpleMapper, T>();
    }
}
