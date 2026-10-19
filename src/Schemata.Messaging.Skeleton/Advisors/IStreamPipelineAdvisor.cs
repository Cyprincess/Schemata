using System.Collections.Generic;
using System.Threading;
using Schemata.Abstractions.Advisors;

namespace Schemata.Messaging.Skeleton.Advisors;

public delegate IAsyncEnumerable<TItem> StreamContinuation<out TItem>(CancellationToken ct);

public interface IStreamPipelineAdvisor<in TRequest, TItem> : IAdvisor where TRequest : IStreamRequest<TItem>
{
    IAsyncEnumerable<TItem> AdviseAsync(StreamExecutionContext context, TRequest request, StreamContinuation<TItem> next, CancellationToken ct);
}
