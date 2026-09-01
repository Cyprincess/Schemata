using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

internal static class FlowResourceRegistration
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
        new(FlowOperations.Start,     typeof(FlowStartProcessHandler),  ResourceMethodScope.Collection),
        new(FlowOperations.Complete,  typeof(CompleteActivityHandler)),
        new(FlowOperations.Correlate, typeof(CorrelateMessageHandler)),
        new(FlowOperations.Signal,    typeof(ThrowSignalHandler),      ResourceMethodScope.Collection),
        new(FlowOperations.Terminate, typeof(TerminateProcessHandler)),
    ];

    internal static readonly ResourceMethodAttribute[] TokenMethods = [new(FlowOperations.Cancel, typeof(CancelTokenHandler))];
}
