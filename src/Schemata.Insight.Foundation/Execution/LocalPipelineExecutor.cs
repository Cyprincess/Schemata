using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Runs the local relational stages a driver could not push, over dictionary rows with explicit
///     stage barriers. Source data lives nested under its source alias
///     (<c>{ "c": { "age": 36 } }</c>) so the shared dict-context expression compiler resolves
///     alias-qualified paths; synthetic compute, group-key, and aggregate columns live as root scalar
///     slots referenced by bare alias. The terminal selection stage flattens to the snake_case
///     response shape.
/// </summary>
public sealed partial class LocalPipelineExecutor
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the in-process pipeline executor that resolves keyed dict-context compilers from the provider.</summary>
    /// <param name="services">The provider resolving keyed dict-context compilers.</param>
    public LocalPipelineExecutor(IServiceProvider services) {
        _services = services;
    }

    /// <summary>
    ///     Normalizes the driver's flat rows into canonical alias-nested rows, then applies the local
    ///     stages in order and flattens the result.
    /// </summary>
    /// <param name="source">The driver's flat snake_case rows.</param>
    /// <param name="sourceAlias">The single source alias the rows belong to.</param>
    /// <param name="stages">The local stages, in plan order.</param>
    /// <param name="ct">A cancellation token.</param>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> RunAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> source,
        string                                                 sourceAlias,
        IReadOnlyList<PlanNode>                                stages,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var rows = Normalize(source, sourceAlias, ct);

        foreach (var stage in stages) {
            rows = Apply(stage, rows, ct);
        }

        await foreach (var row in rows.WithCancellation(ct)) {
            yield return row;
        }
    }

    /// <summary>
    ///     Applies the stages over already-canonical (alias-nested) rows, skipping ingress
    ///     normalization. Used for rows produced by a join, which already carry every source alias.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> RunStagesAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlanNode>                                stages,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        foreach (var stage in stages) {
            rows = Apply(stage, rows, ct);
        }

        await foreach (var row in rows.WithCancellation(ct)) {
            yield return row;
        }
    }

    /// <summary>
    ///     Joins two canonical row streams on a predicate over the merged row. Nested-loop over the
    ///     compiled predicate (the predicate is opaque, so equi-key extraction for a hash join is not
    ///     possible); the buffered side is bounded by the residual scan cap. An unmatched outer row
    ///     carries only its own alias, so the absent side's fields resolve to null.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> JoinAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> left,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> right,
        ParsedExpression                                      on,
        JoinKind                                              kind,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var predicate = Compiler(on.Language)
                       .Compile<IReadOnlyDictionary<string, object?>, bool>(on.Tree)
                       .Compile();

        var buildRight = kind is JoinKind.Inner or JoinKind.Left or JoinKind.Full;
        var probe      = buildRight ? left : right;
        var buffered   = await Buffer(buildRight ? right : left, ct);
        var matched    = new bool[buffered.Count];

        await foreach (var outer in probe.WithCancellation(ct)) {
            var any = false;
            for (var i = 0; i < buffered.Count; i++) {
                var merged = buildRight ? Merge(outer, buffered[i]) : Merge(buffered[i], outer);
                if (!predicate(merged)) {
                    continue;
                }

                any        = true;
                matched[i] = true;
                yield return merged;
            }

            if (!any && kind is JoinKind.Left or JoinKind.Right or JoinKind.Full) {
                yield return outer;
            }
        }

        if (kind is JoinKind.Full) {
            for (var i = 0; i < buffered.Count; i++) {
                if (!matched[i]) {
                    yield return buffered[i];
                }
            }
        }
    }
}
