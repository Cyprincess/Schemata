using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Features;

/// <summary>Feature that wires the event subsystem: type registry and lifecycle observers.</summary>
public sealed class SchemataEventFeature : FeatureBase
{
    /// <summary>Default priority slot for the event feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 40_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddOptions<EventTypeRegistryConfiguration>();
        services.TryAddSingleton<IEventTypeRegistry>(sp => EventTypeRegistryActivator.Build(sp.GetRequiredService<IOptions<EventTypeRegistryConfiguration>>()));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventLifecycleObserver, SchemataEventAuditObserver>());
        services.TryAddSingleton<IEventOutboxPublisher, InProcessEventOutboxPublisher>();
        services.TryAddSingleton<EventOutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<EventOutboxDispatcher>());
    }
}
