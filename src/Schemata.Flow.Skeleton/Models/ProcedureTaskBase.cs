using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Base type for executable BPMN task nodes whose body runs inside the current flow transaction.
/// </summary>
public abstract class ProcedureTaskBase : Activity
{
    /// <summary>Invokes the procedure body for the current token.</summary>
    /// <param name="context">The flow task context supplied by the runtime.</param>
    protected internal abstract ValueTask InvokeAsync(FlowTaskContext context);
}