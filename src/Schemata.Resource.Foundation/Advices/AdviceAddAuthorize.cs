using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceAddAuthorize<TEntity, TRequest> : IResourceAddAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceAddAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(TRequest request, HttpContext context, CancellationToken ct = default) {
        var result = await context.AuthenticateAsync();
        return result is { Succeeded: true };
    }

    #endregion
}
