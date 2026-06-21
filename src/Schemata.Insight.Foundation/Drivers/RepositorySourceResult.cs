using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed class RepositorySourceResult(
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
    IReadOnlyList<FieldDescriptor> schema
) : ISourceResult
{
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; } = rows;

    public IReadOnlyList<FieldDescriptor> Schema { get; } = schema;

    public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }
}
