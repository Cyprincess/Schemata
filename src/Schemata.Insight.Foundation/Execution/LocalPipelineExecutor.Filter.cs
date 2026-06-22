using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed partial class LocalPipelineExecutor
{
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Filter(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        FilterNode                                            filter,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var predicate = Compiler(filter.Predicate.Language)
                       .Compile<IReadOnlyDictionary<string, object?>, bool>(filter.Predicate.Tree)
                       .Compile();

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            if (predicate(row)) {
                yield return row;
            }
        }
    }
}
