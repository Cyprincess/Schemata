using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceDeleteAuthorize<TEntity> : IResourceDeleteAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    #region IResourceDeleteAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(long? id, HttpContext context, CancellationToken ct = default) {
        var result = await context.AuthenticateAsync();
        return result is { Succeeded: true };
    }

    #endregion
}
