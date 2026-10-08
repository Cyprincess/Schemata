using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Messaging.Skeleton;

/// <summary>Capture entry point for <see cref="MessageContext" />, called on the sending side.</summary>
public static class MessageContexts
{
    public const string TenantIdKey = "tenancy.tenant-id";

    public static TenantIdentity Identity(MessageContext? context) {
        if (context is null || !context.Items.TryGetValue(TenantIdKey, out var value)) return TenantIdentity.Host;
        if (value == "host") return TenantIdentity.Host;
        if (!Guid.TryParse(value, out var uid) || uid == Guid.Empty) throw new TenantResolveException();
        return new(uid);
    }

    public static MessageContext Bind(TenantIdentity identity, MessageContext? context = null) {
        var items = context is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>(context.Items);
        items[TenantIdKey] = identity.Uid?.ToString("D") ?? "host";
        return new(items);
    }
    /// <summary>
    ///     Runs every registered <see cref="IMessageContextPropagator" /> against
    ///     <paramref name="source" /> and returns the flattened result.
    /// </summary>
    /// <param name="source">The provider of the scope the message is being sent from.</param>
    public static MessageContext Capture(IServiceProvider source) {
        var items = new Dictionary<string, string?>();

        foreach (var propagator in source.GetServices<IMessageContextPropagator>()) {
            propagator.Capture(items, source);
        }

        return Bind(TenantContext.Current, new(items));
    }
}
