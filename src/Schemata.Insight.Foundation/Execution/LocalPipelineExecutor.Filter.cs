using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed partial class LocalPipelineExecutor
{
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Filter(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        FilterNode                                            filter,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var predicate = ExpressionCache.GetOrAddDelegate(
            Compiler(filter.Predicate.Language)
               .Compile<IReadOnlyDictionary<string, object?>, bool>(filter.Predicate.Tree));

        await foreach (var row in rows.WithCancellation(ct)) {
            if (predicate(row)) {
                yield return row;
            }
        }
    }
}
