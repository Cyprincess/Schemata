using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Foundation.Observers;

/// <summary>
///     Persists <see cref="SchemataEvent" /> audit rows in response to
///     <see cref="IEventBus" /> lifecycle events. Records the initial row on
///     publish and transitions it to the terminal state on consume.
/// </summary>
public sealed class SchemataEventAuditObserver : IEventLifecycleObserver
{
    private readonly JsonSerializerOptions _json;
    private readonly IServiceProvider      _services;

    /// <summary>Initializes an audit observer resolving event repositories from <paramref name="services" />.</summary>
    public SchemataEventAuditObserver(IServiceProvider services, IOptions<JsonSerializerOptions> json) {
        _services = services;
        _json     = json.Value;
    }

    #region IEventLifecycleObserver Members

    public async Task OnPublishedAsync(EventContext context, CancellationToken ct = default) {
        var record = new SchemataEvent {
            EventType     = context.EventType,
            Payload       = context.Payload,
            CorrelationId = context.CorrelationId,
            State         = context.RequiresOutboxDelivery ? EventState.Pending : EventState.Recorded,
        };

        if (context.Source is not null) {
            record.SourceType    = context.Source.GetType().FullName;
            record.Source = context.Source is ICanonicalName named ? named.CanonicalName : null;
            record.SourceTimestamp     = context.Source is IConcurrency stamped ? stamped.Timestamp : null;
        }

        context.Record = record;

        var records = _services.GetRequiredService<IRepository<SchemataEvent>>();
        await records.AddAsync(record, ct);
        await records.CommitAsync(ct);
    }

    public async Task OnDeliveredAsync(EventContext context, CancellationToken ct = default) {
        var records = _services.GetRequiredService<IRepository<SchemataEvent>>();

        var record = context.Record;
        if (record is null && !string.IsNullOrEmpty(context.CorrelationId)) {
            var correlationId = context.CorrelationId;
            record = await records.FirstOrDefaultAsync(q => q.Where(r => r.CorrelationId == correlationId), ct);
        }

        if (record is null) {
            return;
        }

        record.State = EventState.Recorded;
        await records.UpdateAsync(record, ct);
        await records.CommitAsync(ct);
    }

    public async Task OnConsumedAsync(EventContext context, CancellationToken ct = default) {
        var records = _services.GetRequiredService<IRepository<SchemataEvent>>();

        // Cross-process consume: recover the producer's audit row by CorrelationId.
        if (context.Record is null && !string.IsNullOrEmpty(context.CorrelationId)) {
            var correlationId = context.CorrelationId;
            context.Record = await records.FirstOrDefaultAsync(
                q => q.Where(r => r.CorrelationId == correlationId), ct);
        }

        if (context.Record is null) {
            return;
        }

        if (context.Exception != null) {
            context.Record.State       = EventState.Failed;
            context.Record.RecentError = context.Exception.Message;
        } else {
            context.Record.State           = EventState.Succeeded;
            context.Record.ResponsePayload = JsonSerializer.Serialize(context.Result, _json);
        }

        await records.UpdateAsync(context.Record, ct);
        await records.CommitAsync(ct);
    }

    #endregion
}
