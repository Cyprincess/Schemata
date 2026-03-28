using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Workflow.Foundation.Advisors;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation;

/// <summary>
///     Fluent builder for configuring the workflow subsystem.
/// </summary>
public sealed class SchemataWorkflowBuilder
{
    /// <summary>
    ///     Initializes a new instance of the workflow builder.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    public SchemataWorkflowBuilder(IServiceCollection services) { Services = services; }

    /// <summary>
    ///     The service collection used for registering workflow services.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Registers the built-in authorization advisors for Get, Submit, and Raise operations.
    /// </summary>
    /// <param name="scheme">
    ///     Optional ASP.NET Core authentication scheme name. When provided, the workflow controller
    ///     authenticates using this scheme (populating <c>HttpContext.User</c>) before advisors run.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public SchemataWorkflowBuilder WithAuthorization(string? scheme = null) {
        if (!string.IsNullOrWhiteSpace(scheme)) {
            Services.Configure<SchemataWorkflowOptions>(o => o.AuthenticationScheme = scheme);
        }

        // Anonymous advisors run first and set AnonymousGranted in the context when applicable
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IStatusAdvisor, AdviceStatusAnonymous>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<ISubmitAdvisor, AdviceSubmitAnonymous>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRaiseAdvisor, AdviceRaiseAnonymous>());

        // Authorization advisors check AnonymousGranted flag before enforcing access control
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<ISubmitAdvisor, AdviceSubmitAuthorize>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IStatusAdvisor, AdviceStatusAuthorize>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRaiseAdvisor, AdviceRaiseAuthorize>());

        return this;
    }

    /// <summary>
    ///     Registers a state machine for a stateful entity type.
    /// </summary>
    /// <typeparam name="TStateMachine">The state machine implementation type.</typeparam>
    /// <typeparam name="TI">The stateful entity type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public SchemataWorkflowBuilder Use<TStateMachine, TI>()
        where TStateMachine : StateMachineBase<TI>
        where TI : class, IStatefulEntity {
        Services.TryAddScoped<StateMachineBase<TI>, TStateMachine>();

        return this;
    }
}
