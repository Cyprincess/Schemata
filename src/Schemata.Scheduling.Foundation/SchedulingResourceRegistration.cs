using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Registration facts for the Scheduling AIP resources. Transport packages
///     (Http / Grpc) consume the method and operation tables when calling
///     <c>SchemataResourceBuilder.Use&lt;...&gt;</c>, keeping this package free of
///     any cross-component resource dependency.
/// </summary>
internal static class SchedulingResourceRegistration
{
    /// <summary>
    ///     Options-bag key carrying the authentication scheme set through
    ///     <c>SchedulingBuilder.WithAuthorization</c>, read by the transport packages when they
    ///     register the Scheduling resources.
    /// </summary>
    internal const string AuthenticationSchemeKey = "Scheduling:AuthenticationScheme";

    /// <summary>The AIP-136 <c>:run</c> custom method on <see cref="SchemataJob" />.</summary>
    internal static readonly ResourceMethodAttribute[] JobMethods = [
        new(Verbs.Run, typeof(RunJobHandler)),
    ];

    /// <summary>
    ///     The <c>:cancel</c> and <c>:wait</c> custom methods on <see cref="SchemataJobExecution" />,
    ///     following the AIP-136 colon convention and mirroring <c>CancelOperation</c> and
    ///     <c>WaitOperation</c> on the <c>google.longrunning.Operations</c> service.
    /// </summary>
    internal static readonly ResourceMethodAttribute[] ExecutionMethods = [
        new(Verbs.Cancel, typeof(CancelOperationHandler)),
        new(Verbs.Wait,   typeof(WaitOperationHandler)),
    ];

    /// <summary>The standard CRUD verbs exposed for <see cref="SchemataJobExecution" />.</summary>
    internal static readonly Operations[] ExecutionOperations = [Operations.Get, Operations.List, Operations.Delete];

    internal static void RegisterHandlers(IServiceCollection services) {
        services.TryAddScoped<RunJobHandler>();
        services.TryAddScoped<CancelOperationHandler>();
        services.TryAddScoped<WaitOperationHandler>();
        services.Map<SchemataJobExecution, Operation>(map => map.With(e => OperationMapper.FromExecution(e)));
    }
}
