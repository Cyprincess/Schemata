using System.Threading;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

/// <summary>
///     Single serialization point for every <see cref="SchemataJob" /> row write. A handler holds it
///     across its fresh read, write, and timer install; entry or timer manipulation inside that
///     section goes through <see cref="DefaultScheduler.Gate" />, so the fixed nesting order is
///     WriteGate → Gate. Acquiring the two in the reverse order deadlocks.
/// </summary>
internal sealed class SchemataJobWriteGate
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
