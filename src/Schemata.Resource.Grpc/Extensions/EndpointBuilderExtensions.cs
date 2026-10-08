using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Grpc;
using Schemata.Resource.Grpc.Runtime;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods mapping resource gRPC services onto the endpoint route builder.
/// </summary>
public static class EndpointBuilderExtensions
{
    private static readonly MethodInfo? MapGrpcServiceMethod = typeof(GrpcEndpointRouteBuilderExtensions)
                                                              .GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                              .FirstOrDefault(m => m is {
                                                                   Name: nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService),
                                                                   IsGenericMethodDefinition: true,
                                                               } && m.GetParameters().Length == 1);

    /// <summary>
    ///     Maps a resource service for every gRPC-enabled resource in <paramref name="registry" />,
    ///     applying the resource's rate-limit policy and authentication scheme. A resource that
    ///     declares its own <see cref="ResourceRegistration.AuthenticationScheme" /> overrides
    ///     <paramref name="scheme" />.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="registry">The resource registry.</param>
    /// <param name="scheme">
    ///     The authentication scheme required by resources that declare none of their own, or
    ///     <see langword="null" /> to leave those resources unauthenticated.
    /// </param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapSchemataGrpcResources(
        this IEndpointRouteBuilder endpoints,
        ResourceRegistry          registry,
        string?                    scheme = null
    ) {
        foreach (var resource in registry.Resources) {
            if (!GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            var service = typeof(ResourceService<,,,>).MakeGenericType(resource.Entity, resource.Request, resource.Detail, resource.Summary);

            if (MapGrpcService(endpoints, service) is not IEndpointConventionBuilder builder) {
                throw new InvalidOperationException(
                    $"Mapping gRPC service '{service.FullName}' for resource '{resource.Entity.FullName}' returned no endpoint builder; "
                    + "the resource's endpoints would be silently dropped.");
            }

            var quota = resource.Entity.GetCustomAttribute<RateLimitPolicyAttribute>();
            if (quota is not null) {
                builder.RequireRateLimiting(quota.PolicyName);
            }

            var required = resource.AuthenticationScheme ?? scheme;
            if (!string.IsNullOrWhiteSpace(required)) {
                // A declared scheme means authentication must succeed; [Anonymous] operations are
                // exempted per method by the service method provider, matching the HTTP filter.
                var policy = new AuthorizationPolicyBuilder(required)
                            .RequireAuthenticatedUser()
                            .Build();
                builder.RequireAuthorization(policy);
            }
        }

        return endpoints;
    }

    private static object? MapGrpcService(IEndpointRouteBuilder endpoints, Type serviceType) {
        if (MapGrpcServiceMethod is null) {
            throw new InvalidOperationException(
                $"{typeof(GrpcEndpointRouteBuilderExtensions).FullName}.{nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService)} "
                + "could not be located; the gRPC server dependencies may be missing or incompatible.");
        }

        try {
            return MapGrpcServiceMethod.MakeGenericMethod(serviceType).Invoke(null, [endpoints]);
        } catch (TargetInvocationException e) when (e.InnerException is not null) {
            // Propagate the actual binder failure instead of swallowing the resource's endpoints.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }
}
