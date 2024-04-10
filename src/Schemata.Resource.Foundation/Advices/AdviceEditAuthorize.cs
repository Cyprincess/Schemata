using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceEditAuthorize<TEntity, TRequest> : IResourceEditAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    private readonly SchemataResourceOptions _options;

    public AdviceEditAuthorize(IOptions<SchemataResourceOptions> options) {
        _options = options.Value;
    }

    #region IResourceEditAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        long              id,
        TRequest          request,
        HttpContext       context,
        CancellationToken ct = default) {
        var resource = _options.Resources[typeof(TEntity)];

        if (resource.Edit is null) {
            return true;
        }

        var result = await context.AuthorizeAsync(resource.Edit, resource, nameof(resource.Edit));
        return result is { Succeeded: true };
    }

    #endregion
}
