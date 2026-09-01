using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Preserves the public Insight facade while dispatching queries through the registered request dispatcher.
/// </summary>
public sealed class DefaultInsightService : IInsightService
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the facade over the registered request dispatcher.</summary>
    /// <param name="services">The provider resolving a per-call scope for the request dispatcher.</param>
    public DefaultInsightService(IServiceProvider services) {
        _services = services;
    }

    #region IInsightService Members

    public async ValueTask<QueryInsightResponse> QueryAsync(
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default
    ) {
        request.Principal = principal;
        using var scope      = _services.CreateScope();
        var       dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        return await dispatcher.SendAsync<QueryInsightRequest, QueryInsightResponse>(request, ct);
    }

    #endregion
}
