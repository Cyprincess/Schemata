using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Event.Foundation.Advisors;

public sealed class AdvicePublishAudit : IEventPublishAdvisor
{
    public const int DefaultOrder = Orders.Base;

    private readonly IRepository<SchemataEvent> _records;

    public AdvicePublishAudit(IRepository<SchemataEvent> records) { _records = records; }

    #region IEventPublishAdvisor Members

    public int Order => DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        EventContext      @event,
        CancellationToken ct = default
    ) {
        var record = new SchemataEvent {
            EventType     = @event.EventType,
            Payload       = @event.Payload,
            CorrelationId = @event.CorrelationId,
            State         = EventState.Recorded,
        };

        @event.Record = record;

        await _records.AddAsync(record, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
