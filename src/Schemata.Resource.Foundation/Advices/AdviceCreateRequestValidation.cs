using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceCreateRequestValidation<TEntity, TRequest>(IServiceProvider services) : IResourceCreateRequestAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceCreateAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext       context,
        CancellationToken ct = default) {
        if (ctx.Has<SuppressCreateRequestValidation>()) {
            return true;
        }

        var errors = new List<KeyValuePair<string, string>>();
        var pass = await Advices<IValidationAsyncAdvice<TRequest>>.AdviseAsync(services, ctx, Operations.Create, request, errors, ct);
        if (pass) {
            return true;
        }

        throw new ValidationException(errors);
    }

    #endregion
}
