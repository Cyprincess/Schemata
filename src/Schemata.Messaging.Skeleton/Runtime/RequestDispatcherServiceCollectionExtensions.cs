using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary><see cref="IServiceCollection" /> extensions for the in-process request dispatcher.</summary>
public static class RequestDispatcherServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="InProcessRequestDispatcher" /> as the scoped dispatch owner and aliases
    ///     <see cref="IRequestDispatcher" />, <see cref="ICommandDispatcher" /> and
    ///     <see cref="IQueryDispatcher" /> to that same instance. The <c>TryAdd</c> shape lets a
    ///     transport registered earlier (e.g. RabbitMQ) keep owning the interface slots while this
    ///     still self-registers the concrete type for inbound consumption. Ownership of all three
    ///     slots is published as <see cref="InProcessDispatcherOwnership" /> so later registrations
    ///     can follow the host's dispatcher choice without resolving it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddInProcessRequestDispatcher(this IServiceCollection services) {
        services.TryAddScoped<InProcessRequestDispatcher>();

        var owns = !services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IRequestDispatcher))
                && !services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(ICommandDispatcher))
                && !services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IQueryDispatcher));
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        if (owns) services.TryAddSingleton<InProcessDispatcherOwnership>();

        return services;
    }
}
