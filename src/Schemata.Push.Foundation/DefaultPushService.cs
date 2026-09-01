using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Foundation;

/// <summary>
///     Dispatcher-backed facade for immediate push fan-out. The facade awaits the complete
///     <see cref="IRequestDispatcher.SendAsync{TRequest,TResponse}" /> result before yielding,
///     so observers see every transport's outcome together rather than per-completion.
/// </summary>
public sealed class DefaultPushService(IRequestDispatcher dispatcher) : IPushService
{
    public async IAsyncEnumerable<TransportResult> SendAsync(
        PushContext                                context,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var results = await dispatcher.SendAsync<SendPushRequest, ImmutableArray<TransportResult>>(
            new(context), ct);
        foreach (var result in results) {
            yield return result;
        }
    }
}
