using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Resource method and operation declarations for the host-selected process and token entity
///     types.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
internal static class FlowResourceRegistration<TProcess, TToken>
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
{
    /// <summary>
    ///     Options-bag key carrying the authentication scheme set through
    ///     <c>SchemataFlowBuilder.WithAuthorization</c>, read by the transport packages when they
    ///     register the Flow resources.
    /// </summary>
    internal const string AuthenticationSchemeKey = "Flow:AuthenticationScheme";

    internal static readonly Operations[] ProcessOperations = [Operations.Get, Operations.List];

    internal static readonly Operations[] TokenOperations = [Operations.Get, Operations.List];

    internal static readonly Operations[] TransitionOperations = [Operations.Get, Operations.List];

    internal static readonly ResourceMethodAttribute[] ProcessMethods = [
        new(FlowOperations.Start,     typeof(FlowStartProcessHandler), ResourceMethodScope.Collection),
        new(FlowOperations.Complete,  typeof(CompleteActivityHandler)),
        new(FlowOperations.Correlate, typeof(CorrelateMessageHandler)),
        new(FlowOperations.Signal,    typeof(ThrowSignalHandler),              ResourceMethodScope.Collection),
        new(FlowOperations.Terminate, typeof(TerminateProcessHandler)),
    ];

    internal static readonly ResourceMethodAttribute[] TokenMethods = [new(FlowOperations.Cancel, typeof(CancelTokenHandler<TToken>))];
}
