using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Automatonymous;
using Automatonymous.Graphing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Skeleton.Managers;

/// <summary>
/// Default implementation of <see cref="IWorkflowManager{TWorkflow,TTransition,TResponse}"/> and <see cref="IWorkflowManager"/>.
/// </summary>
/// <typeparam name="TWorkflow">The workflow entity type.</typeparam>
/// <typeparam name="TTransition">The transition entity type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
/// <remarks>
/// Coordinates repositories, state machines, and mappers to manage the full workflow lifecycle
/// including creation, event raising, transition recording, and response mapping.
/// </remarks>
public class SchemataWorkflowManager<TWorkflow, TTransition, TResponse> : IWorkflowManager<TWorkflow, TTransition, TResponse>,
                                                                          IWorkflowManager
    where TWorkflow : SchemataWorkflow, new()
    where TTransition : SchemataTransition, new()
    where TResponse : WorkflowResponse
{
    private readonly ISimpleMapper            _mapper;
    private readonly ITypeResolver            _resolver;
    private readonly IServiceProvider         _sp;
    private readonly IRepository<TTransition> _transitions;
    private readonly IRepository<TWorkflow>   _workflows;

    /// <summary>
    /// Initializes a new instance of the workflow manager.
    /// </summary>
    /// <param name="sp">The service provider for resolving repositories and state machines.</param>
    /// <param name="mapper">The mapper for converting workflow details to response objects.</param>
    /// <param name="transitions">The transition repository.</param>
    /// <param name="workflows">The workflow repository.</param>
    /// <param name="resolver">The type resolver for entity types.</param>
    public SchemataWorkflowManager(
        IServiceProvider         sp,
        ISimpleMapper            mapper,
        IRepository<TTransition> transitions,
        IRepository<TWorkflow>   workflows,
        ITypeResolver            resolver
    ) {
        _sp          = sp;
        _mapper      = mapper;
        _transitions = transitions;
        _workflows   = workflows;
        _resolver    = resolver;
    }

    #region IWorkflowManager Members

    Task<Type?> IWorkflowManager.GetInstanceTypeAsync(string type, CancellationToken ct) {
        return GetInstanceTypeAsync(type, ct);
    }

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

    async Task<SchemataWorkflow?> IWorkflowManager.CreateAsync(
        IStatefulEntity?  instance,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        return await CreateAsync(instance, principal, ct);
    }

    async Task<SchemataWorkflow?> IWorkflowManager.CreateAsync(Type instance, long id, CancellationToken ct) {
        return await CreateAsync(instance, id, ct);
    }

    Task IWorkflowManager.RaiseAsync<TEvent>(
        SchemataWorkflow? workflow,
        TEvent            @event,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        return RaiseAsync((TWorkflow?)workflow, @event, principal, ct);
    }

    Task IWorkflowManager.RaiseAsync<TEvent>(long id, TEvent @event, CancellationToken ct) {
        return RaiseAsync(id, @event, ct);
    }

    async Task<object?> IWorkflowManager.MapAsync(
        SchemataWorkflow?       workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal,
        CancellationToken       ct
    ) {
        return await MapAsync((TWorkflow?)workflow, options, principal, ct);
    }

    #endregion

    #region IWorkflowManager<TWorkflow,TTransition,TResponse> Members

    /// <inheritdoc />
    public Task<Type?> GetInstanceTypeAsync(string type, CancellationToken ct = default) {
        if (!_resolver.TryResolveType(type, out var it)) {
            return Task.FromResult<Type?>(null);
        }

        return Task.FromResult(it);
    }

    /// <inheritdoc />
    public virtual async Task<TWorkflow?> FindAsync(long id, CancellationToken ct = default) {
        return await _workflows.SingleOrDefaultAsync(q => q.Where(w => w.Id == id), ct);
    }

    /// <inheritdoc />
    public virtual async Task<IStatefulEntity?> FindInstanceAsync(long id, CancellationToken ct = default) {
        var workflow = await FindAsync(id, ct);
        if (workflow is null) {
            return null;
        }

        return await GetInstanceAsync(workflow, ct);
    }

    /// <inheritdoc />
    public virtual async Task<IStatefulEntity?> GetInstanceAsync(TWorkflow workflow, CancellationToken ct = default) {
        var (type, repository) = await ResolveRepositoryAsync(workflow);
        if (type is null || repository is null) {
            return null;
        }

        if (!typeof(IStatefulEntity).IsAssignableFrom(type)) {
            return null;
        }

        var instance = await repository.SingleOrDefaultAsync<IStatefulEntity>(e => e.Id == workflow.InstanceId, ct);

        return (IStatefulEntity?)instance;
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TTransition> ListTransitionsAsync(long id, CancellationToken ct = default) {
        return _transitions.ListAsync(q => q.Where(p => p.WorkflowId == id), ct);
    }

    /// <inheritdoc />
    public virtual async Task<TWorkflow?> CreateAsync(
        IStatefulEntity?  instance,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        if (instance is null) {
            throw new ArgumentNullException(nameof(instance));
        }

        var type = instance.GetType();

        var repository = ResolveRepository(type);
        if (repository is null) {
            return null;
        }

        await repository.AddAsync(instance, ct);
        await repository.CommitAsync(ct);

        return await CreateAsync(type, instance.Id, ct);
    }

    /// <inheritdoc />
    public virtual async Task<TWorkflow?> CreateAsync(Type instance, long id, CancellationToken ct = default) {
        var workflow = new TWorkflow { InstanceId = id, InstanceType = instance.FullName! };

        await _workflows.AddAsync(workflow, ct);
        await _workflows.CommitAsync(ct);

        return workflow;
    }

    /// <inheritdoc />
    public virtual async Task RaiseAsync<TEvent>(long id, TEvent @event, CancellationToken ct = default)
        where TEvent : class, IEvent {
        var workflow = await FindAsync(id, ct);

        await RaiseAsync(workflow, @event, null, ct);
    }

    /// <inheritdoc />
    public virtual async Task RaiseAsync<TEvent>(
        TWorkflow?        workflow,
        TEvent            @event,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    )
        where TEvent : class, IEvent {
        if (workflow is null) {
            throw new ArgumentNullException(nameof(workflow));
        }

        @event.UpdatedById ??= principal?.GetUserId();
        @event.UpdatedBy   ??= principal?.GetDisplayName();

        var instance = await GetInstanceAsync(workflow, ct);
        if (instance is null) {
            return;
        }

        var (type, machine) = await ResolveStateMachineAsync(workflow);
        if (type is null || machine is null) {
            return;
        }

        var method = typeof(StateMachineBaseExtensions).GetMethod(nameof(StateMachineBaseExtensions.RaiseEventAsync),
                                                                  BindingFlags.Static | BindingFlags.Public);
        var invoke = method!.MakeGenericMethod(type, @event.GetType());

        await (Task)invoke.Invoke(null, [machine, instance, @event, ct])!;
    }

    /// <inheritdoc />
    public virtual async Task<TResponse?> MapAsync(
        TWorkflow?              workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal = null,
        CancellationToken       ct        = default
    ) {
        if (workflow is null) {
            throw new ArgumentNullException(nameof(workflow));
        }

        var history = await ListTransitionsAsync(workflow.Id, ct).ToListAsync(ct);

        var instance = await GetInstanceAsync(workflow, ct);
        if (instance is null) {
            return null;
        }

        var (type, machine) = await ResolveStateMachineAsync(workflow);
        if (type is null || machine is null) {
            return null;
        }

        var visit = typeof(StateMachineBaseExtensions).GetMethod(nameof(StateMachineBaseExtensions.GetGraphAsync),
                                                                 BindingFlags.Static | BindingFlags.Public);
        var graph = await (Task<StateMachineGraph>)visit!.MakeGenericMethod(type).Invoke(null, [machine, ct])!;

        var next = typeof(StateMachineBaseExtensions).GetMethod(nameof(StateMachineBaseExtensions.GetNextEventsAsync),
                                                                BindingFlags.Static | BindingFlags.Public);
        var events = await (Task<IEnumerable<string>>)next!.MakeGenericMethod(type)
                                                           .Invoke(null, [machine, instance, ct])!;

        var details = new WorkflowDetails<TWorkflow, TTransition> {
            Workflow    = workflow,
            Instance    = instance,
            Graph       = graph,
            Events      = events.ToList(),
            Transitions = history,
        };

        var response = _mapper.Map<TResponse>(details);

        return response;
    }

    #endregion

    /// <summary>
    /// Resolves the entity type and its repository for the given workflow.
    /// </summary>
    /// <param name="workflow">The workflow whose instance type to resolve.</param>
    /// <returns>A tuple of the resolved type and repository, either of which may be <see langword="null"/>.</returns>
    protected virtual async Task<(Type?, IRepository?)> ResolveRepositoryAsync(TWorkflow workflow) {
        var it = await GetInstanceTypeAsync(workflow.InstanceType);
        if (it is null) {
            return (null, null);
        }

        return (it, ResolveRepository(it));
    }

    /// <summary>
    /// Resolves the repository for the given entity type from the service provider.
    /// </summary>
    /// <param name="type">The entity type.</param>
    /// <returns>The repository, or <see langword="null"/> if it cannot be cast.</returns>
    protected virtual IRepository? ResolveRepository(Type type) {
        var rt      = typeof(IRepository<>).MakeGenericType(type);
        var service = _sp.GetRequiredService(rt);

        return service as IRepository;
    }

    /// <summary>
    /// Resolves the entity type and its state machine for the given workflow.
    /// </summary>
    /// <param name="workflow">The workflow whose state machine to resolve.</param>
    /// <returns>A tuple of the resolved type and state machine, either of which may be <see langword="null"/>.</returns>
    protected virtual async Task<(Type?, StateMachine?)> ResolveStateMachineAsync(TWorkflow workflow) {
        var it = await GetInstanceTypeAsync(workflow.InstanceType);
        if (it is null) {
            return (null, null);
        }

        return (it, ResolveStateMachine(it));
    }

    /// <summary>
    /// Resolves the state machine for the given entity type from the service provider.
    /// </summary>
    /// <param name="type">The entity type.</param>
    /// <returns>The state machine instance.</returns>
    protected virtual StateMachine? ResolveStateMachine(Type type) {
        var mt      = typeof(StateMachineBase<>).MakeGenericType(type);
        var machine = (StateMachine)_sp.GetRequiredService(mt);

        return machine;
    }
}
