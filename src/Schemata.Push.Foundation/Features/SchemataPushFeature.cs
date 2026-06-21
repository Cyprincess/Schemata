using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Features;

/// <summary>
///     Registers the push service, subscription manager, and the
///     <see cref="SchemataPushSubscription" /> resource. Transports are contributed separately
///     through <c>UsePush(p =&gt; p.AddTransport&lt;T&gt;())</c>. Subscription endpoints are exposed
///     by whatever resource transport (HTTP/gRPC) the host activates.
/// </summary>
public sealed class SchemataPushFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Push feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 100_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped<IPushService, DefaultPushService>();
        services.TryAddScoped<IPushSubscriptionManager, DefaultPushSubscriptionManager>();
    }
}
