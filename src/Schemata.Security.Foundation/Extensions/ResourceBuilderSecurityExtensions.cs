using System;
using System.Collections.Generic;
using Schemata.Core.Building;
using Schemata.Security.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class ResourceBuilderSecurityExtensions
{
    public static TBuilder WithAuthentication<TBuilder>(this TBuilder builder, string? scheme = null)
        where TBuilder : IResourceBuilder {
        var registration = GetRegistration(builder);
        registration.SetScheme(scheme);
        registration.AddAuthentication(builder.Services);
        return builder;
    }

    public static TBuilder WithAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IResourceBuilder {
        GetRegistration(builder).AddAuthorization(builder.Services);
        return builder;
    }

    private static ResourceSecurityRegistration GetRegistration(IResourceBuilder builder) {
        var registrations = builder.Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration));
        if (registrations is null || !registrations.TryGetValue(builder, out var registration)) {
            throw new InvalidOperationException($"No {nameof(ResourceSecurityRegistration)} is registered for {builder.GetType().FullName}.");
        }

        return registration;
    }
}
