using System;
using System.Collections.Generic;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     A source driver's streamed result: nested string-keyed rows and the schema describing them.
/// </summary>
public interface ISourceResult : IAsyncDisposable
{
    /// <summary>The streamed rows.</summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; }

    /// <summary>The schema describing each row.</summary>
    IReadOnlyList<FieldDescriptor> Schema { get; }
}
