using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton;

public interface IMessageExecutionScopeRunner
{
    ValueTask<T> RunAsync<T>(
        MessageContext? context,
        Func<IServiceProvider, CancellationToken, ValueTask<T>> work,
        CancellationToken ct = default);
}
