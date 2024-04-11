using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Entities;
using Schemata.Validation.FluentValidation.Advices;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceAddValidation<TEntity, TRequest> : IResourceAddAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    private readonly IServiceProvider     _services;
    private readonly IValidator<TRequest> _validator;

    public AdviceAddValidation(IServiceProvider services, IValidator<TRequest> validator) {
        _services  = services;
        _validator = validator;
    }

    #region IResourceAddAdvice<TEntity,TRequest> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(TRequest request, HttpContext context, CancellationToken ct = default) {
        var errors = new List<KeyValuePair<string, string>>();
        var pass = await Advices<IValidationAsyncAdvice<TRequest>>.AdviseAsync(_services, _validator, request, errors, ct);
        if (pass) {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.Headers.Append("Error", errors.Select(kv => $"{kv.Key}={kv.Value}").ToArray());

        return false;
    }

    #endregion
}
