using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Messaging.Skeleton.Runtime;

public sealed class MessageExecutionScopeFactory(IServiceScopeFactory scopes) : IMessageExecutionScopeFactory
{
    public ValueTask<MessageExecutionScope> CreateAsync(MessageContext? context, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new MessageExecutionScope(scopes.CreateAsyncScope(), static () => new EmptyLease()));
    }

    private sealed class EmptyLease : IDisposable
    {
        public void Dispose() { }
    }
}
