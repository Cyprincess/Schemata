using System.Threading;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

/// <summary>
///     Single serialization point for every <see cref="SchemataJob" /> row write. A scheduling
///     writer holds it across its fresh read, write, and timer install; entry or timer manipulation
///     inside that section goes through <c>DefaultScheduler&lt;TJob,TExecution&gt;.Gate</c>, so the fixed nesting
///     order is WriteGate → Gate. Acquiring the two in the reverse order deadlocks.
/// </summary>
internal sealed class SchemataJobWriteGate
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
