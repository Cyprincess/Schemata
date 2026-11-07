using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton.Runtime;

public sealed class MessageExecutionScopeRunner(IMessageExecutionScopeFactory scopes) : IMessageExecutionScopeRunner
{
    public async ValueTask<T> RunAsync<T>(
        MessageContext? context,
        Func<IServiceProvider, CancellationToken, ValueTask<T>> work,
        CancellationToken ct = default) {
        var scope = await scopes.CreateAsync(context, ct);
        T result;
        try {
            await scope.RestoreAsync(context, ct);
            result = await work(scope.Services, ct);
        } catch (Exception error) {
            try {
                await scope.DisposeAsync();
            } catch (Exception cleanup) {
                throw new AggregateException(error, cleanup);
            }
            throw;
        }
        await scope.DisposeAsync();
        return result;
    }
}
