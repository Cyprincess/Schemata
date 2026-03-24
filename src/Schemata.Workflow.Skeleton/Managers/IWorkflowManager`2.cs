using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Skeleton.Managers;

/// <summary>
/// Strongly-typed workflow manager that operates on specific workflow, transition, and response types.
/// </summary>
/// <typeparam name="TWorkflow">The workflow entity type.</typeparam>
/// <typeparam name="TTransition">The transition entity type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
public interface IWorkflowManager<TWorkflow, TTransition, TResponse>
    where TWorkflow : SchemataWorkflow
    where TTransition : SchemataTransition
    where TResponse : WorkflowResponse
{
    /// <summary>
    /// Resolves the CLR type for the given instance type name.
    /// </summary>
    /// <param name="type">The fully qualified type name of the entity.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resolved <see cref="Type"/>, or <see langword="null"/> if not found.</returns>
    Task<Type?> GetInstanceTypeAsync(string type, CancellationToken ct = default);

    /// <summary>
    /// Finds a workflow by its identifier.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The workflow, or <see langword="null"/> if not found.</returns>
    Task<TWorkflow?> FindAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Finds the stateful entity instance associated with the given workflow identifier.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity instance, or <see langword="null"/> if not found.</returns>
    Task<IStatefulEntity?> FindInstanceAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Gets the stateful entity instance linked to the given workflow.
    /// </summary>
    /// <param name="workflow">The workflow.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity instance, or <see langword="null"/> if not found.</returns>
    Task<IStatefulEntity?> GetInstanceAsync(TWorkflow workflow, CancellationToken ct = default);

    /// <summary>
    /// Lists all transitions recorded for the given workflow.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of transitions.</returns>
    IAsyncEnumerable<TTransition> ListTransitionsAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new workflow for the given stateful entity instance.
    /// </summary>
    /// <param name="instance">The entity instance to associate with the new workflow.</param>
    /// <param name="principal">The authenticated user creating the workflow.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created workflow, or <see langword="null"/> on failure.</returns>
    Task<TWorkflow?> CreateAsync(
        IStatefulEntity?  instance,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>
    /// Creates a new workflow record for an existing entity identified by type and identifier.
    /// </summary>
    /// <param name="instance">The CLR type of the entity.</param>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created workflow, or <see langword="null"/> on failure.</returns>
    Task<TWorkflow?> CreateAsync(Type instance, long id, CancellationToken ct = default);

    /// <summary>
    /// Raises an event on the specified workflow, causing a state transition.
    /// </summary>
    /// <typeparam name="TEvent">The event data type.</typeparam>
    /// <param name="workflow">The workflow to transition.</param>
    /// <param name="event">The event data.</param>
    /// <param name="principal">The authenticated user raising the event.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RaiseAsync<TEvent>(
        TWorkflow?        workflow,
        TEvent            @event,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    )
        where TEvent : class, IEvent;

    /// <summary>
    /// Raises an event on a workflow identified by its identifier.
    /// </summary>
    /// <typeparam name="TEvent">The event data type.</typeparam>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="event">The event data.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RaiseAsync<TEvent>(long id, TEvent @event, CancellationToken ct = default)
        where TEvent : class, IEvent;

    /// <summary>
    /// Maps a workflow and its state machine graph into a strongly-typed response.
    /// </summary>
    /// <param name="workflow">The workflow to map.</param>
    /// <param name="options">Workflow configuration options specifying the response types.</param>
    /// <param name="principal">The authenticated user, used for context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The mapped response, or <see langword="null"/> if the workflow cannot be resolved.</returns>
    Task<TResponse?> MapAsync(
        TWorkflow?              workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal = null,
        CancellationToken       ct        = default
    );
}
