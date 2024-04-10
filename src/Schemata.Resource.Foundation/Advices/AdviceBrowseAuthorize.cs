using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceBrowseAuthorize<TEntity> : IResourceBrowseAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly SchemataResourceOptions _options;

    public AdviceBrowseAuthorize(IOptions<SchemataResourceOptions> options) {
        _options = options.Value;
    }

    #region IResourceBrowseAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        string?           query,
        long?             cursor,
        int               size,
        HttpContext       context,
        CancellationToken ct = default) {
        var resource = _options.Resources[typeof(TEntity)];

        if (resource.Browse is null) {
            return true;
        }

        var result = await context.AuthorizeAsync(resource.Browse, resource, nameof(resource.Browse));
        return result is { Succeeded: true };
    }

    #endregion
}
