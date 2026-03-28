using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Security.Foundation.Features;

public sealed class SchemataSecurityFeature : FeatureBase
{
    public const int DefaultPriority = Orders.Extension;

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
        var configure = configurators.PopOrDefault<SchemataSecurityOptions>();
        services.Configure(configure);

        services.TryAddScoped<IPermissionResolver, DefaultPermissionResolver>();
        services.TryAddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
        services.TryAddScoped(typeof(IAccessProvider<,>), typeof(DefaultAccessProvider<,>));
        services.TryAddScoped(typeof(IEntitlementProvider<,>), typeof(DefaultEntitlementProvider<,>));
    }
}
