using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

public sealed class StreamValidationPipelineAdvisor<TRequest, TItem>(Operations operation)
    : IStreamPipelineAdvisor<TRequest, TItem> where TRequest : IStreamRequest<TItem>
{
    public int Order => SecurityOrders.Validation;
    public async IAsyncEnumerable<TItem> AdviseAsync(StreamExecutionContext context, TRequest request,
        StreamContinuation<TItem> next, [EnumeratorCancellation] CancellationToken ct) {
        var errors = new List<ErrorFieldViolation>();
        if (await Advisor.For<IValidationAdvisor<TRequest>>().RunAsync(context.Advice, operation, request, errors, ct) == AdviseResult.Block) {
            throw new ValidationException(errors);
        }
        await foreach (var item in next(ct).WithCancellation(ct)) yield return item;
    }
}
