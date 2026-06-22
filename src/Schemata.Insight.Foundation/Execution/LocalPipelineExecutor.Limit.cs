using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed partial class LocalPipelineExecutor
{
    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Limit(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        LimitNode                                             limit,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var skip    = Math.Max(0, limit.Skip ?? 0);
        var take    = limit.Take;
        var emitted = 0;
        var seen    = 0;

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            if (seen++ < skip) {
                continue;
            }

            if (take is { } max && emitted >= max) {
                yield break;
            }

            emitted++;
            yield return row;
        }
    }
}
