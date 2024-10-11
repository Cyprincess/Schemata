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

public sealed class AdviceEditRequestValidation<TEntity, TRequest> : IResourceEditRequestAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    private readonly IServiceProvider _sp;

    public AdviceEditRequestValidation(IServiceProvider sp) {
        _sp = sp;
    }

    #region IResourceEditRequestAdvice<TEntity,TRequest> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        TRequest          request,
        HttpContext       http,
        CancellationToken ct = default) {
        var only = request is IValidation { ValidateOnly: true };

        if (ctx.Has<SuppressEditRequestValidation>()) {
            if (only) {
                throw new NoContentException();
            }

            return true;
        }

        var errors = new List<KeyValuePair<string, string>>();
        var pass = await Advices<IValidationAsyncAdvice<TRequest>>.AdviseAsync(_sp, ctx, Operations.Update, request, errors, ct);

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
