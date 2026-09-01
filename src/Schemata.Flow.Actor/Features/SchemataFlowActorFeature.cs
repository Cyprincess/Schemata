using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Features;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Actor.Handlers;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using CorrelateMessageRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;

namespace Schemata.Flow.Actor.Features;

/// <summary>
///     Installs the Flow.Actor bridge: replaces the unkeyed default handler of every write-path
///     Flow command with <see cref="ActorSerializingHandler{TRequest,TResult}" /> and registers the
///     shared <see cref="RequestDispatchingActor" /> under the <c>"flow"</c> route, so every entry
///     point that resolves the unkeyed <see cref="IRequestHandler{TRequest,TResponse}" /> — facade,
///     <see cref="IRequestDispatcher" />, <see cref="ICommandDispatcher" />, transports, event and
///     timer bridges alike — gets per-process serialization without changing which type it resolves.
/// </summary>
/// <remarks>
///     <see cref="StartProcessRequest" /> and the fan-out <c>ThrowSignalRequest</c> coordinator are
///     deliberately left unwrapped (§5.9): the former has no existing process key to race on, and
///     the latter performs no write of its own — it only enumerates candidates and re-enters this
///     same route per target through the already-wrapped <see cref="DeliverSignalRequest" />.
/// </remarks>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataActorFeature>]
public sealed class SchemataFlowActorFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow.Actor feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 600_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.Replace(ServiceDescriptor.Transient<
            IRequestHandler<CompleteActivityRequest, ProcessSnapshot>,
            ActorSerializingHandler<CompleteActivityRequest, ProcessSnapshot>>());
        services.Replace(ServiceDescriptor.Transient<
            IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>,
            ActorSerializingHandler<CorrelateMessageRequest, ProcessSnapshot>>());
        services.Replace(ServiceDescriptor.Transient<
            IRequestHandler<RunEventRequest, ProcessSnapshot>,
            ActorSerializingHandler<RunEventRequest, ProcessSnapshot>>());
        services.Replace(ServiceDescriptor.Transient<
            IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>,
            ActorSerializingHandler<DeliverSignalRequest, SignalDeliveryResult>>());
        services.Replace(ServiceDescriptor.Transient<
            IRequestHandler<TerminateProcessRequest, ProcessSnapshot>,
            ActorSerializingHandler<TerminateProcessRequest, ProcessSnapshot>>());
        services.Replace(ServiceDescriptor.Transient<
            IRequestHandler<CancelTokenRequest, ProcessSnapshot>,
            ActorSerializingHandler<CancelTokenRequest, ProcessSnapshot>>());

        new SchemataActorBuilder(schemata, services).Register<RequestDispatchingActor>(
            "flow", FlowConstants.Handlers.Default);
    }
}
