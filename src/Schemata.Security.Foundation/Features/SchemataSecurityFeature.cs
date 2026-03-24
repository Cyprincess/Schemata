using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation.Features;

/// <summary>
///     Schemata feature that registers default security providers for access control and entitlement filtering.
/// </summary>
/// <remarks>
///     Registers <see cref="DefaultAccessProvider{T, TContext}"/> and
///     <see cref="DefaultEntitlementProvider{T, TContext}"/> as open-generic fallbacks.
///     Custom providers registered before this feature will not be overwritten.
/// </remarks>
public sealed class SchemataSecurityFeature : FeatureBase
{
    public const int DefaultPriority = SchemataConstants.Orders.Extension;

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
        services.TryAddScoped(typeof(IAccessProvider<,>), typeof(DefaultAccessProvider<,>));
        services.TryAddScoped(typeof(IEntitlementProvider<,>), typeof(DefaultEntitlementProvider<,>));
    }
}
