using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed partial class LocalPipelineExecutor
{
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Order(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        OrderNode                                             order,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var keys = _services.GetRequiredService<IOrderCompiler>().Parse(order.OrderBy);

        var buffer = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            buffer.Add(row);
        }

        IOrderedEnumerable<IReadOnlyDictionary<string, object?>>? sorted = null;
        foreach (var key in keys) {
            var path = string.Join('.', key.Path);
            sorted = (sorted, key.Descending) switch {
                (null, false)     => buffer.OrderBy(r => Resolve(r, path), RowComparer.Instance),
                (null, true)      => buffer.OrderByDescending(r => Resolve(r, path), RowComparer.Instance),
                ({ } s, false)    => s.ThenBy(r => Resolve(r, path), RowComparer.Instance),
                ({ } s, true)     => s.ThenByDescending(r => Resolve(r, path), RowComparer.Instance),
            };
        }

        foreach (var row in (IEnumerable<IReadOnlyDictionary<string, object?>>?)sorted ?? buffer) {
            yield return row;
        }
    }
}
