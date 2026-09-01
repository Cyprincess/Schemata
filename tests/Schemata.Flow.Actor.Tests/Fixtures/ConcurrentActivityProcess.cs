using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;

namespace Schemata.Flow.Actor.Tests.Fixtures;

/// <summary>
///     Single-activity state-machine process: one live token sits at <see cref="Doing" /> until a
///     <see cref="CompleteActivityRequest" /> advances it — parking it at its own gateway, the
///     state machine's normal shape for a task awaiting an external correlation (RFC state-machine
///     semantics; see <c>NoneTaskProgressionShould.Advance_Parks_None_Task_At_Its_Gateway</c>).
///     That first advance is the only one that produces a real
///     <see cref="Schemata.Flow.Skeleton.Entities.SchemataProcessTransition" /> row; every other
///     concurrent attempt reloads the token, finds it already parked (<c>WaitingAtName</c> set),
///     and returns the current snapshot unchanged — the engine's own idempotent no-op, not a race
///     artifact. <see cref="Approved" /> is never actually correlated by this suite; only the first
///     advance's parking transition is exercised.
/// </summary>
public sealed class ConcurrentActivityProcess : ProcessDefinition
{
    public ConcurrentActivityProcess() {
        this.Start().Go(Doing);
        this.During(Doing).Await(this.On(Approved).Go(Done));
        this.During(Done).End();
    }

    public UserTask Doing    { get; } = null!;
    public UserTask Done     { get; } = null!;
    public Message  Approved { get; } = null!;
}