using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Messaging.Skeleton.Runtime;

public sealed class MessageExecutionScopeFactory(IServiceScopeFactory scopes) : IMessageExecutionScopeFactory
{
    public ValueTask<MessageExecutionScope> CreateAsync(MessageContext? context, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var identity = MessageContexts.Identity(context);
        if (identity != TenantIdentity.Host) throw new TenantResolveException();
        return ValueTask.FromResult(new MessageExecutionScope(scopes.CreateAsyncScope(), identity));
    }
}
