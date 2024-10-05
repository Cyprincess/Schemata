using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceEditRequestValidation<TEntity, TRequest>(IServiceProvider services) : IResourceEditRequestAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceUpdateAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        TRequest          request,
        HttpContext       context,
        CancellationToken ct = default) {
        var only = request is IValidation { ValidateOnly: true };

        if (ctx.Has<SuppressEditRequestValidation>()) {
            if (only) {
                throw new NoContentException();
            }

            return true;
        }

        var errors = new List<KeyValuePair<string, string>>();
        var pass = await Advices<IValidationAsyncAdvice<TRequest>>.AdviseAsync(services, ctx, Operations.Update, request, errors, ct);
        if (!pass) {
            throw new ValidationException(errors);
        }

        if (only) {
            throw new NoContentException();
        }

        return true;
    }

    #endregion
}
