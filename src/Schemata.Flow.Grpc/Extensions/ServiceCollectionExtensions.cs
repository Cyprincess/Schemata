using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Flow.Foundation;
using Schemata.Flow.Grpc;
using Schemata.Flow.Grpc.Services;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Foundation;
using Schemata.Transport.Grpc;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods exposing the Flow entities over the gRPC resource transport.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Declares process, token and transition as gRPC resources with the Flow method set, and
    ///     registers the process-definition service plus its proto contributor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag the resource registry lives on.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlowGrpcResources(
        this IServiceCollection services,
        SchemataOptions         schemata
    ) {
        FlowResourceRegistration.RegisterHandlers(services);

        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(FlowResourceRegistration.AuthenticationSchemeKey),
        };
        resources.Use<SchemataProcess, SchemataProcess, SchemataProcess, SchemataProcess>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration.ProcessOperations;
                resource.Methods    = FlowResourceRegistration.ProcessMethods;
            });
        resources.Use<SchemataProcessToken, SchemataProcessToken, SchemataProcessToken, SchemataProcessToken>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration.TokenOperations;
                resource.Methods    = FlowResourceRegistration.TokenMethods;
            });
        resources.Use<SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition>(
            [GrpcResourceAttribute.Name],
            resource => resource.Operations = FlowResourceRegistration.TransitionOperations);

        services.TryAddScoped<ProcessDefinitionService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTypeContributor, FlowProtoTypeContributor>());

        return services;
    }
}
