using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation;
using Schemata.Abstractions;
using Schemata.Actor.Foundation.Features;
using Schemata.Core;
using Schemata.Push.Skeleton;
using Schemata.Core.Features;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Actor.Internal;
using Schemata.Push.Foundation;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Features;

namespace Schemata.Push.Actor.Features;

/// <summary>
///     Installs the Push.Actor bridge: replaces the unkeyed default handler of both
///     subscription-scoped commands with <see cref="ActorSerializingHandler{TRequest,TResult}" />
///     and registers the shared <see cref="Schemata.Actor.Foundation.Internal.RequestDispatchingActor" />
///     under the <c>"push"</c> route keyed by the subscription triple, so every entry point that
///     resolves the unkeyed handler — facade, dispatcher, transports — serializes concurrent writers
///     to the same subscription.
/// </summary>
/// <remarks>
///     <see cref="SendPushRequest" /> is deliberately left unwrapped: its fan-out across transports
///     is deliberate parallelism, not a race. The read-path queries do not write and need no mailbox.
/// </remarks>
[DependsOn<SchemataPushFeature>]
[DependsOn<SchemataActorFeature>]
public sealed class SchemataPushActorFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Push.Actor feature.</summary>
    public const int DefaultPriority = SchemataPushFeature.DefaultPriority + 600_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.Replace(ServiceDescriptor.Scoped<
            IRequestHandler<AddPushSubscriptionRequest, PushSubscriptionResult>,
            ActorSerializingHandler<AddPushSubscriptionRequest, PushSubscriptionResult>>());
        services.Replace(ServiceDescriptor.Scoped<
            IRequestHandler<RemovePushSubscriptionRequest, Unit>,
            ActorSerializingHandler<RemovePushSubscriptionRequest, Unit>>());

        new SchemataActorBuilder(schemata, services).Register<Schemata.Actor.Foundation.Internal.RequestDispatchingActor>(
            "push", PushConstants.Handlers.Default);
    }
}