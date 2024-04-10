using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceReadAuthorize<TEntity> : IResourceReadAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly SchemataResourceOptions _options;

    public AdviceReadAuthorize(IOptionsMonitor<SchemataResourceOptions> options) {
        _options = options.CurrentValue;
    }

    #region IResourceReadAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(long id, HttpContext context, CancellationToken ct = default) {
        var resource = _options.Resources[typeof(TEntity)];

        if (resource.Read is null) {
            return true;
        }

        var result = await context.AuthorizeAsync(resource.Read, resource, nameof(resource.Read));
        return result is { Succeeded: true };
    }

    #endregion
}
