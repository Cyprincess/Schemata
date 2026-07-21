using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     A repository source result that retains its execution scope while its lazy rows are available.
/// </summary>
public sealed class RepositorySourceResult : ISourceResult
{
    private readonly IAsyncDisposable? _scope;

    /// <summary>Initializes a repository result without an owned execution scope.</summary>
    /// <param name="rows">The lazy source rows.</param>
    /// <param name="schema">The schema describing each row.</param>
    public RepositorySourceResult(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<FieldDescriptor>                        schema
    ) : this(rows, schema, null) { }

    /// <summary>
    ///     Initializes a repository result. The supplied execution scope remains active until this result is
    ///     disposed so repository-backed row enumeration can resolve scoped dependencies.
    /// </summary>
    /// <param name="rows">The lazy source rows.</param>
    /// <param name="schema">The schema describing each row.</param>
    /// <param name="scope">The scope that owns the repository query dependencies.</param>
    internal RepositorySourceResult(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<FieldDescriptor>                        schema,
        IAsyncDisposable?                                     scope
    ) {
        Rows   = rows;
        Schema = schema;
        _scope = scope;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; }

    /// <inheritdoc />
    public IReadOnlyList<FieldDescriptor> Schema { get; }

    /// <summary>Disposes the execution scope retained for lazy repository row enumeration.</summary>
    public ValueTask DisposeAsync() { return _scope?.DisposeAsync() ?? ValueTask.CompletedTask; }
}
