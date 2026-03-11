using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceCreateRequestValidation<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        var only = request is IValidation { ValidateOnly: true };

        if (ctx.Has<SuppressCreateRequestValidation>()) {
            if (only) {
                throw new NoContentException();
            }

            return AdviseResult.Continue;
        }

        var errors = new List<KeyValuePair<string, string>>();
        switch (await Advisor.For<IValidationAdvisor<TRequest>>()
                             .RunAsync(ctx, Operations.Create, request, errors, ct)) {
            case AdviseResult.Block:
                throw new ValidationException(errors);
            case AdviseResult.Handle:
                return AdviseResult.Handle;
            case AdviseResult.Continue:
            default:
                break;
        }

        if (only) {
            throw new NoContentException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
