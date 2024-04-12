using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton.Managers;

public interface IWorkflowManager
{
    Task<Type?> GetInstanceTypeAsync(string type, CancellationToken ct = default);

    Task<SchemataWorkflow?> FindAsync(long id, CancellationToken ct = default);

    Task<IStatefulEntity?> FindInstanceAsync(long id, CancellationToken ct = default);

    Task<IStatefulEntity?> GetInstanceAsync(SchemataWorkflow workflow, CancellationToken ct = default);

    IAsyncEnumerable<SchemataTransition> ListTransitionsAsync(long id, CancellationToken ct = default);

    Task<SchemataWorkflow?> CreateAsync(IStatefulEntity instance, CancellationToken ct = default);

    Task<SchemataWorkflow?> CreateAsync(Type instance, long id, CancellationToken ct = default);

    Task RaiseAsync<TEvent>(SchemataWorkflow workflow, TEvent @event, CancellationToken ct = default)
        where TEvent : class, IEvent;

    Task<object?> MapAsync(
        SchemataWorkflow        workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal = null,
        CancellationToken       ct        = default);
}
