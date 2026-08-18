using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods exposing the Flow entities over the HTTP resource transport.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Declares process, token and transition as HTTP resources with the Flow method set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag the resource registry lives on.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlowHttpResources(
        this IServiceCollection services,
        SchemataOptions         schemata
    ) {
        FlowResourceRegistration.RegisterHandlers(services);

        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(FlowResourceRegistration.AuthenticationSchemeKey),
        };
        resources.Use<SchemataProcess, SchemataProcess, SchemataProcess, SchemataProcess>(
            [HttpResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration.ProcessOperations;
                resource.Methods    = FlowResourceRegistration.ProcessMethods;
            });
        resources.Use<SchemataProcessToken, SchemataProcessToken, SchemataProcessToken, SchemataProcessToken>(
            [HttpResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration.TokenOperations;
                resource.Methods    = FlowResourceRegistration.TokenMethods;
            });
        resources.Use<SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition>(
            [HttpResourceAttribute.Name],
            resource => resource.Operations = FlowResourceRegistration.TransitionOperations);

        return services;
    }
}
