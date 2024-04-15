using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceRequestAuthorize<TEntity> : IResourceRequestAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly SchemataResourceOptions _options;

    public AdviceRequestAuthorize(IOptions<SchemataResourceOptions> options) {
        _options = options.Value;
    }

    #region IResourceRequestAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        HttpContext       context,
        Operations        operation,
        CancellationToken ct = default) {
        var resource = _options.Resources[typeof(TEntity)];

        var policy = operation switch {
            Operations.List   => resource.List,
            Operations.Get    => resource.Get,
            Operations.Create => resource.Create,
            Operations.Update => resource.Update,
            Operations.Delete => resource.Delete,
            var _             => null,
        };

        if (policy is null) {
            return true;
        }

        var result = await context.AuthorizeAsync(policy, resource, operation);
        return result is { Succeeded: true };
    }

    #endregion
}
