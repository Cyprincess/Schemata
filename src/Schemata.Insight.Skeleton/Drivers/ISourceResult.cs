using System;
using System.Collections.Generic;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Skeleton.Drivers;

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
