using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Messaging;

public sealed class TenantMessageContextPropagator : IMessageContextPropagator
{
    public void Capture(IDictionary<string, string?> items, IServiceProvider source) {
        items[TenancyConstants.Messaging.TenantIdKey] = TenantContext.Current.Uid?.ToString("D") ?? "host";
    }

    internal static TenantIdentity Identity(IReadOnlyDictionary<string, string?>? items) {
        if (items is null || !items.TryGetValue(TenancyConstants.Messaging.TenantIdKey, out var value)) return TenantIdentity.Host;
        if (value == "host") return TenantIdentity.Host;
        if (!Guid.TryParse(value, out var uid) || uid == Guid.Empty) throw new TenantResolveException();
        return new(uid);
    }

    public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        if (Identity(items) != TenantContext.Current) throw new TenantResolveException();
        return ValueTask.CompletedTask;
    }
}
