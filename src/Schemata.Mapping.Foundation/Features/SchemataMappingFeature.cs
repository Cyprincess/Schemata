using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Mapping.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Mapping.Foundation.Features;

/// <summary>
///     Feature that registers a concrete <see cref="ISimpleMapper" /> implementation as a scoped service.
/// </summary>
/// <typeparam name="T">The mapper implementation type.</typeparam>
public sealed class SchemataMappingFeature<T> : SchemataMappingFeature
    where T : class, ISimpleMapper
{
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
        services.TryAddScoped<ISimpleMapper, T>();
    }
}

/// <summary>
///     Base feature class for mapping subsystem features.
/// </summary>
public abstract class SchemataMappingFeature : FeatureBase
{
    public const int DefaultPriority = Orders.Extension + 30_000_000;
}
