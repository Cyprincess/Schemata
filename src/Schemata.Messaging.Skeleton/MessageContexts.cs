using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Messaging.Skeleton;

/// <summary>Capture entry point for <see cref="MessageContext" />, called on the sending side.</summary>
public static class MessageContexts
{
    /// <summary>
    ///     Runs every registered <see cref="IMessageContextPropagator" /> against
    ///     <paramref name="source" /> and returns the flattened result.
    /// </summary>
    /// <remarks>
    ///     With no propagator registered the returned context is empty and every restore downstream
    ///     is a no-op, so the caller never has to ask which optional packages are present.
    /// </remarks>
    /// <param name="source">The provider of the scope the message is being sent from.</param>
    public static MessageContext Capture(IServiceProvider source) {
        var items = new Dictionary<string, string?>();

        foreach (var propagator in source.GetServices<IMessageContextPropagator>()) {
            propagator.Capture(items, source);
        }

        return new MessageContext(items);
    }
}
