using System;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation;

/// <summary>Discovers the single dispatcher handler shape used by AIP-136 custom methods.</summary>
public static class ResourceMethodHandlerHelper
{
    /// <summary>Finds the closed request-handler interface implemented by a custom-method handler.</summary>
    public static Type? FindHandlerInterface(Type handler) {
        foreach (var iface in handler.GetInterfaces()) {
            if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IRequestHandler<,>)) {
                continue;
            }

            var arguments = iface.GetGenericArguments();
            if (typeof(IRequestPrincipal).IsAssignableFrom(arguments[0])
             && typeof(ICanonicalName).IsAssignableFrom(arguments[1])) {
                return iface;
            }
        }

        return null;
    }

    /// <summary>Describes a custom-method request handler for one registered resource type.</summary>
    public static ResourceMethodHandlerDescriptor? Describe(Type entity, Type handler) {
        var iface = FindHandlerInterface(handler);
        if (iface is null) {
            return null;
        }

        var arguments = iface.GetGenericArguments();
        return new(entity, arguments[0], arguments[1], handler);
    }
}
