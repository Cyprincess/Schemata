using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;

namespace Schemata.Transport.Grpc;

public sealed class StreamService<TRequest, TItem>(IServiceProvider services)
    where TRequest : class, IStreamRequest<TItem> where TItem : class
{
    internal async Task InvokeAsync(TRequest request, IServerStreamWriter<TItem> writer, ServerCallContext context) {
        var dispatcher = services.GetService<IStreamDispatcher>()
            ?? throw new RpcException(new(StatusCode.Unimplemented, "Streaming dispatch is not installed."));
        System.Collections.Generic.IAsyncEnumerable<TItem> sequence;
        try { sequence = dispatcher.Stream<TRequest, TItem>(request, context.GetHttpContext().User, context.CancellationToken); }
        catch (NotSupportedException error) { throw new RpcException(new(StatusCode.Unimplemented, error.Message)); }
        await foreach (var item in sequence.WithCancellation(context.CancellationToken)) {
            await writer.WriteAsync(item, context.CancellationToken);
        }
    }
}
