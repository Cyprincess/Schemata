using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Validation.FluentValidation.Advisors;

public sealed class RequestValidationPipelineAdvisor<TRequest, TResponse>(Operations operation)
    : IRequestPipelineAdvisor<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public int Order => SecurityOrders.Validation;

    public async Task<TResponse> AdviseAsync(AdviceContext context, TRequest request,
        RequestHandlerContinuation<TResponse> next, CancellationToken ct) {
        var errors = new List<ErrorFieldViolation>();
        if (await Advisor.For<IValidationAdvisor<TRequest>>().RunAsync(context, operation, request, errors, ct) == AdviseResult.Block) {
            throw new ValidationException(errors);
        }
        return await next(ct);
    }
}
