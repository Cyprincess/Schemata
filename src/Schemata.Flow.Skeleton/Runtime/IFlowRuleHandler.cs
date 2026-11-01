using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Flow.Skeleton.Runtime;

public interface IFlowRuleHandler<TInput, TResult>
{
    ValueTask<BusinessRuleResult<TResult>> EvaluateAsync(TInput input, FlowTaskContext context, CancellationToken ct);
}
