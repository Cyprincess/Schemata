using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;

namespace Schemata.Scheduling.Foundation;

internal delegate ValueTask<JobExecutionScope> JobExecutionScopeFactory(CancellationToken ct);

internal sealed record JobExecutionScope(MessageExecutionScope? Scope, IMessageExecutionScopeRunner? Runner, MessageContext? Context);

internal static class JobExecutionScopes
{
    public static JobExecutionScopeFactory Plain(IServiceScopeFactory scopes) => ct => {
        ct.ThrowIfCancellationRequested();
        var scope = new MessageExecutionScope(new AsyncServiceScope(scopes.CreateScope()));
        return ValueTask.FromResult(new JobExecutionScope(scope, null, null));
    };

    public static JobExecutionScopeFactory Messaging(IMessageExecutionScopeFactory scopes, MessageContext context) => _ =>
        ValueTask.FromResult(new JobExecutionScope(null, new MessageExecutionScopeRunner(scopes), context));
}
