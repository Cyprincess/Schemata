using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Automatonymous;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Skeleton.Managers;

public class SchemataWorkflowManager<TWorkflow, TTransition, TResponse> : IWorkflowManager<TWorkflow, TTransition, TResponse>,
                                                                          IWorkflowManager
    where TTransition : SchemataTransition
    where TWorkflow : SchemataWorkflow
    where TResponse : WorkflowResponse
{
    private readonly ISimpleMapper            _mapper;
    private readonly ITypeResolver            _resolver;
    private readonly IServiceProvider         _services;
    private readonly IRepository<TTransition> _transitions;
    private readonly IRepository<TWorkflow>   _workflows;

    public SchemataWorkflowManager(
        ISimpleMapper            mapper,
        IRepository<TTransition> transitions,
        IRepository<TWorkflow>   workflows,
        ITypeResolver            resolver,
        IServiceProvider         services) {
        _mapper      = mapper;
        _transitions = transitions;
        _workflows   = workflows;
        _resolver    = resolver;
        _services    = services;
    }

    #region IWorkflowManager Members

    async Task<SchemataWorkflow?> IWorkflowManager.FindAsync(long id, CancellationToken ct) {
        return await FindAsync(id, ct);
    }

    Task<IStatefulEntity?> IWorkflowManager.FindInstanceAsync(long id, CancellationToken ct) {
        return FindInstanceAsync(id, ct);
    }

    Task<IStatefulEntity?> IWorkflowManager.GetInstanceAsync(SchemataWorkflow workflow, CancellationToken ct) {
        return GetInstanceAsync((TWorkflow)workflow, ct);
    }

    IAsyncEnumerable<SchemataTransition> IWorkflowManager.ListTransitionsAsync(long id, CancellationToken ct) {
        return ListTransitionsAsync(id, ct);
    }

    Task IWorkflowManager.RaiseAsync<TEvent>(SchemataWorkflow workflow, TEvent @event, CancellationToken ct) {
        return RaiseAsync((TWorkflow)workflow, @event, ct);
    }

    async Task<object?> IWorkflowManager.MapAsync(
        SchemataWorkflow        workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal,
        CancellationToken       ct) {
        return await MapAsync((TWorkflow)workflow, options, principal, ct);
    }

    #endregion

    #region IWorkflowManager<TWorkflow,TTransition,TResponse> Members

    public virtual async Task<TWorkflow?> FindAsync(long id, CancellationToken ct = default) {
        return await _workflows.SingleOrDefaultAsync(q => q.Where(w => w.Id == id), ct);
    }

    public virtual async Task<IStatefulEntity?> FindInstanceAsync(long id, CancellationToken ct = default) {
        var workflow = await FindAsync(id, ct);
        if (workflow == null) {
            return null;
        }

        return await GetInstanceAsync(workflow, ct);
    }

    public virtual async Task<IStatefulEntity?> GetInstanceAsync(TWorkflow workflow, CancellationToken ct = default) {
        var (type, repository) = ResolveRepository(workflow);
        if (type == null || repository == null) {
            return null;
        }

        if (!typeof(IStatefulEntity).IsAssignableFrom(type)) {
            return null;
        }

        var instance = await repository.SingleOrDefaultAsync<IStatefulEntity>(e => e.Id == workflow.InstanceId, ct);

        return (IStatefulEntity?)instance;
    }

    public virtual IAsyncEnumerable<TTransition> ListTransitionsAsync(long id, CancellationToken ct = default) {
        return _transitions.ListAsync(q => q.Where(p => p.WorkflowId == id), ct);
    }

    public virtual async Task RaiseAsync<TEvent>(TWorkflow workflow, TEvent @event, CancellationToken ct = default)
        where TEvent : class, IEvent {
        var instance = await GetInstanceAsync(workflow, ct);
        if (instance == null) {
            return;
        }

        var (type, machine) = ResolveStateMachine(workflow);
        if (type == null || machine == null) {
            return;
        }

        var method = typeof(StateMachineBaseExtensions).GetMethod(nameof(StateMachineBaseExtensions.RaiseEventAsync), BindingFlags.Static);
        var invoke = method!.MakeGenericMethod(type, typeof(TEvent));

        await (Task)invoke.Invoke(null, [machine, instance, @event, ct])!;
    }

    public virtual async Task<TResponse?> MapAsync(
        TWorkflow               workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal = null,
        CancellationToken       ct        = default) {
        var transitions = await ListTransitionsAsync(workflow.Id, ct).ToListAsync(ct);

        var instance = await GetInstanceAsync(workflow, ct);
        if (instance == null) {
            return null;
        }

        var (type, machine) = ResolveStateMachine(workflow);
        if (type == null || machine == null) {
            return null;
        }

        var method = typeof(StateMachineBaseExtensions).GetMethod(nameof(StateMachineBaseExtensions.GetNextEventsAsync), BindingFlags.Static);
        var invoke = method!.MakeGenericMethod(type);

        var events = await (Task<IEnumerable<string>>)invoke.Invoke(null, [machine, instance, ct])!;

        var details = new WorkflowDetails<TWorkflow, TTransition> {
            Workflow    = workflow,
            Instance    = instance,
            Events      = events.ToList(),
            Transitions = transitions,
        };

        var response = _mapper.Map<TResponse>(details);

        return response;
    }

    #endregion

    protected virtual (Type?, IRepository?) ResolveRepository(TWorkflow workflow) {
        if (!_resolver.TryResolveType(workflow.InstanceType, out var type)) {
            return (null, null);
        }

        var rt         = typeof(IRepository<>).MakeGenericType(type!);
        var repository = (IRepository)_services.GetRequiredService(rt);

        return (type, repository);
    }

    protected virtual (Type?, StateMachine?) ResolveStateMachine(TWorkflow workflow) {
        if (!_resolver.TryResolveType(workflow.Type, out var type)) {
            return (null, null);
        }

        var mt      = typeof(StateMachineBase<>).MakeGenericType(type!);
        var machine = (StateMachine)_services.GetRequiredService(mt);

        return (type, machine);
    }
}
