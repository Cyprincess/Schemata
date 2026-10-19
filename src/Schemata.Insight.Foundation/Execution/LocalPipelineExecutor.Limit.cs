using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton.Plan;

namespace Schemata.Insight.Foundation.Execution;

public sealed partial class LocalPipelineExecutor
{
    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Limit(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        LimitNode                                             limit,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var skip    = Math.Max(0, limit.Skip ?? 0);
        var take    = limit.Take;

        // A nonpositive take yields nothing, so the upstream is never opened; a reached take stops
        // after the row is handed over, before the loop pulls one more.
        if (take is { } max && max <= 0) {
            yield break;
        }

        var seen    = 0;
        var emitted = 0;
        await foreach (var row in rows.WithCancellation(ct)) {
            if (seen++ < skip) {
                continue;
            }

            yield return row;

            if (++emitted >= take) {
                yield break;
            }
        }
    }
}
