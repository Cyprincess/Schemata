using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Event.Foundation.Advisors;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;

namespace Schemata.Event.Foundation.Features;

public sealed class SchemataEventFeature : FeatureBase
{
    public override int Priority => SchemataConstants.Orders.Extension + 20_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped<IEventBus, InProcessEventBus>();
        services.TryAddSingleton<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        services.TryAddScoped<HandlerResolver>();
        services.TryAddScoped<IEventDispatchContext, EventDispatchContext>();

        services.TryAddScoped<IEventPublishAdvisor, AdvicePublishAudit>();
        services.TryAddScoped<IEventConsumeAdvisor, AdviceConsumeAudit>();
    }
}
