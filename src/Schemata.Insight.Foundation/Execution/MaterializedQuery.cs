using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     A single-pass, unpaged query result. Disposing the result closes the source driver result that
///     supplies its rows.
/// </summary>
public sealed class MaterializedQuery : IAsyncDisposable
{
    private readonly ISourceResult? _sourceResult;

    internal MaterializedQuery(
        ImmutableArray<FieldDescriptor>                        schema,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        ISourceResult?                                         sourceResult = null
    ) {
        Schema        = schema;
        Rows          = rows;
        _sourceResult = sourceResult;
    }

    /// <summary>The schema available when the materialized result is opened.</summary>
    public ImmutableArray<FieldDescriptor> Schema { get; }

    /// <summary>The unpaged row stream.</summary>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; }

    /// <summary>Closes the source driver result supplying this materialized query.</summary>
    public ValueTask DisposeAsync() {
        return _sourceResult?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
