using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceBreadAuthorize<TEntity> : IResourceBreadAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    #region IResourceBreadAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(HttpContext context, CancellationToken ct = default) {
        var result = await context.AuthenticateAsync();
        return result is { Succeeded: true };
    }

    #endregion
}
