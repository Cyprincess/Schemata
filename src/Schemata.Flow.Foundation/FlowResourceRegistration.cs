using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Foundation;

internal static class FlowResourceRegistration
{
    internal static readonly Operations[] ProcessOperations = [Operations.Get, Operations.List];

    internal static readonly Operations[] TokenOperations = [Operations.Get, Operations.List];

    internal static readonly Operations[] TransitionOperations = [Operations.Get, Operations.List];

    internal static readonly ResourceMethodAttribute[] ProcessMethods = [
        new("start", typeof(FlowStartProcessHandler), ResourceMethodScope.Collection),
        new("complete", typeof(CompleteActivityHandler)),
        new("correlate", typeof(FlowCorrelateMessageHandler)),
        new("signal", typeof(FlowThrowSignalHandler), ResourceMethodScope.Collection),
        new("terminate", typeof(TerminateProcessHandler)),
    ];

    internal static readonly ResourceMethodAttribute[] TokenMethods = [new("cancel", typeof(CancelTokenHandler))];

    internal static void RegisterHandlers(IServiceCollection services) {
        services.TryAddScoped<FlowSourceLoader>();
        services.TryAddScoped<FlowStartProcessHandler>();
        services.TryAddScoped<CompleteActivityHandler>();
        services.TryAddScoped<FlowCorrelateMessageHandler>();
        services.TryAddScoped<FlowThrowSignalHandler>();
        services.TryAddScoped<TerminateProcessHandler>();
        services.TryAddScoped<CancelTokenHandler>();
    }
}
