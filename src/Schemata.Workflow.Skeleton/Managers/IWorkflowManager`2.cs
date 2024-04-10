using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Skeleton.Managers;

public interface IWorkflowManager<TWorkflow, TTransition, TResponse>
    where TTransition : SchemataTransition
    where TWorkflow : SchemataWorkflow
    where TResponse : WorkflowResponse
{
    Task<TWorkflow?> FindAsync(long id, CancellationToken ct = default);

    Task<IStatefulEntity?> FindInstanceAsync(long id, CancellationToken ct = default);

    Task<IStatefulEntity?> GetInstanceAsync(TWorkflow workflow, CancellationToken ct = default);

    IAsyncEnumerable<TTransition> ListTransitionsAsync(long id, CancellationToken ct = default);

    Task RaiseAsync<TEvent>(TWorkflow workflow, TEvent @event, CancellationToken ct = default)
        where TEvent : class, IEvent;

    Task<TResponse?> MapAsync(
        TWorkflow               workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal = null,
        CancellationToken       ct        = default);
}
