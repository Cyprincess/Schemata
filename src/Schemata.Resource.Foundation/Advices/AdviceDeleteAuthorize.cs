using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceDeleteAuthorize<TEntity> : IResourceDeleteAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly SchemataResourceOptions _options;

    public AdviceDeleteAuthorize(IOptions<SchemataResourceOptions> options) {
        _options = options.Value;
    }

    #region IResourceDeleteAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(long id, HttpContext context, CancellationToken ct = default) {
        var resource = _options.Resources[typeof(TEntity)];

        if (resource.Delete is null) {
            return true;
        }

        var result = await context.AuthorizeAsync(resource.Delete, resource, nameof(resource.Delete));
        return result is { Succeeded: true };
    }

    #endregion
}
