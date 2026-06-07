using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
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
    private readonly JsonSerializerOptions      _json;
    private readonly IRepository<SchemataEvent> _records;

    public SchemataEventAuditObserver(IRepository<SchemataEvent> records, IOptions<JsonSerializerOptions> json) {
        _records = records;
        _json    = json.Value;
    }

    #region IEventLifecycleObserver Members

    public async Task OnPublishedAsync(EventContext context, CancellationToken ct = default) {
        var record = new SchemataEvent {
            EventType     = context.EventType,
            Payload       = context.Payload,
            CorrelationId = context.CorrelationId,
            State         = EventState.Recorded,
        };

        context.Record = record;

        await _records.AddAsync(record, ct);
        await _records.CommitAsync(ct);
    }

    public async Task OnConsumedAsync(EventContext context, CancellationToken ct = default) {
        // Cross-process consume: producer wrote the row in another process; recover by CorrelationId.
        if (context.Record == null && !string.IsNullOrEmpty(context.CorrelationId)) {
            var correlationId = context.CorrelationId;
            context.Record = await _records.FirstOrDefaultAsync(
                q => q.Where(r => r.CorrelationId == correlationId), ct);
        }

        if (context.Record == null) {
            return;
        }

        if (context.Exception != null) {
            context.Record.State       = EventState.Failed;
            context.Record.RecentError = context.Exception.Message;
        } else {
            context.Record.State           = EventState.Succeeded;
            context.Record.ResponsePayload = JsonSerializer.Serialize(context.Result, _json);
        }

        await _records.UpdateAsync(context.Record, ct);
        await _records.CommitAsync(ct);
    }

    #endregion
}
