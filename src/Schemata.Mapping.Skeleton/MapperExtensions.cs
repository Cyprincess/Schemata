using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Mapping.Skeleton;

public static class MapperExtensions
{
    public static IEnumerable<T> Each<T>(
        this ISimpleMapper  mapper,
        IEnumerable<object> source,
        CancellationToken   ct = default) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();

            var result = mapper.Map<T>(item);
            if (result is null) {
                continue;
            }

            yield return result;
        }
    }

    public static async IAsyncEnumerable<T> EachAsync<T>(
        this ISimpleMapper                         mapper,
        IAsyncEnumerable<object>                   source,
        [EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            var result = mapper.Map<T>(item);
            if (result is null) {
                continue;
            }

            yield return result;
        }
    }
}
