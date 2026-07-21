using System;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Shared reflection helper for AIP-136 custom-method handler types, consumed by
///     the HTTP and gRPC transports to resolve a handler's closed
///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" /> interface.
/// </summary>
public static class ResourceMethodHandlerHelper
{
    /// <summary>
    ///     Finds the closed
    ///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" /> interface
    ///     implemented by a custom-method handler type.
    /// </summary>
    /// <param name="handler">The custom-method handler type to inspect.</param>
    /// <returns>
    ///     The closed handler interface, or <see langword="null" /> when the type does
    ///     not implement <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" />.
    /// </returns>
    public static Type? FindHandlerInterface(Type handler) {
        foreach (var iface in handler.GetInterfaces()) {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>)) {
                return iface;
            }
        }
        return null;
    }
}
