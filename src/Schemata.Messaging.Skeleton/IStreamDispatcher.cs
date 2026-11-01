using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;

namespace Schemata.Messaging.Skeleton;

public interface IStreamDispatcher
{
    IAsyncEnumerable<TItem> Stream<TRequest, TItem>(TRequest request, ClaimsPrincipal? principal = null, CancellationToken ct = default)
        where TRequest : IStreamRequest<TItem>;
}
