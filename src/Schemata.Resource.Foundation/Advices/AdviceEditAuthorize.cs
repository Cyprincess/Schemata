using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceEditAuthorize<TEntity, TRequest> : IResourceEditAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceEditAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        long?             id,
        TRequest          request,
        HttpContext       context,
        CancellationToken ct = default) {
        var result = await context.AuthenticateAsync();
        return result is { Succeeded: true };
    }

    #endregion
}
