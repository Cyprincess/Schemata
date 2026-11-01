using System.Collections.Generic;
using System.Threading;

namespace Schemata.Messaging.Skeleton;

public interface IStreamRequestHandler<in TRequest, out TItem> where TRequest : IStreamRequest<TItem>
{
    IAsyncEnumerable<TItem> HandleAsync(TRequest request, StreamExecutionContext context, CancellationToken ct = default);
}
