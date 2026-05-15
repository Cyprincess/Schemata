using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Event.Foundation.Advisors;

public sealed class AdviceConsumeAudit : IEventConsumeAdvisor
{
    public const     int                   DefaultOrder = Orders.Base;
    private readonly JsonSerializerOptions _json;

    private readonly IRepository<SchemataEvent> _records;

    public AdviceConsumeAudit(IRepository<SchemataEvent> records, IOptions<JsonSerializerOptions> json) {
        _records = records;
        _json    = json.Value;
    }

    #region IEventConsumeAdvisor Members

    public int Order => DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        EventContext      @event,
        CancellationToken ct = default
    ) {
        if (@event.Record == null) {
            return AdviseResult.Continue;
        }

        if (@event.Exception != null) {
            @event.Record.State     = EventState.Failed;
            @event.Record.Exception = @event.Exception.Message;
        } else {
            @event.Record.State           = EventState.Succeeded;
            @event.Record.ResponsePayload = JsonSerializer.Serialize(@event.Result, _json);
        }

        await _records.UpdateAsync(@event.Record, ct);
        await _records.CommitAsync(ct);

        return AdviseResult.Continue;
    }

    #endregion
}
