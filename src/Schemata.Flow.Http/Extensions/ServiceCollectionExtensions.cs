using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Core.Building;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods exposing the Flow entities over the HTTP resource transport.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Declares process, token and transition as HTTP resources with the Flow method set for the
    ///     host-selected entity types.
    /// </summary>
    /// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
    /// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
    /// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag the resource registry lives on.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlowHttpResources<TProcess, TToken, TTransition>(
        this IServiceCollection services,
        SchemataOptions         schemata
    )
        where TProcess : SchemataProcess
        where TToken : SchemataProcessToken
        where TTransition : SchemataProcessTransition {
        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(FlowResourceRegistration<TProcess, TToken>.AuthenticationSchemeKey),
        };
        resources.Use<TProcess, TProcess, TProcess, TProcess>(
            [HttpResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration<TProcess, TToken>.ProcessOperations;
                resource.Methods    = FlowResourceRegistration<TProcess, TToken>.ProcessMethods;
            });
        resources.Use<TToken, TToken, TToken, TToken>(
            [HttpResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration<TProcess, TToken>.TokenOperations;
                resource.Methods    = FlowResourceRegistration<TProcess, TToken>.TokenMethods;
            });
        resources.Use<TTransition, TTransition, TTransition, TTransition>(
            [HttpResourceAttribute.Name],
            resource => resource.Operations = FlowResourceRegistration<TProcess, TToken>.TransitionOperations);

        return services;
    }
}
