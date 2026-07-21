using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed partial class LocalPipelineExecutor
{
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Compute(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        ComputeNode                                           compute,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var fields = compute.Fields
                            .Select(field => (field.Alias,
                                              Value: Compiler(field.Expression.Language)
                                                    .Compile<IReadOnlyDictionary<string, object?>, object>(field.Expression.Tree)
                                                    .Compile()))
                            .ToArray();

        await foreach (var row in rows.WithCancellation(ct)) {
            var next = new Dictionary<string, object?>(row, StringComparer.Ordinal);
            foreach (var (alias, value) in fields) {
                next[alias] = value(row);
            }

            yield return next;
        }
    }
}
