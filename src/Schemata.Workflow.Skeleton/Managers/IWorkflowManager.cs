using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton.Managers;

/// <summary>
///     Non-generic workflow manager interface providing type-erased access to workflow operations.
/// </summary>
/// <remarks>
///     This interface is resolved at runtime by the workflow controller when the concrete generic types are not known at
///     compile time.
/// </remarks>
public interface IWorkflowManager
{
    /// <summary>
    ///     Resolves the CLR type for the given instance type name.
    /// </summary>
    /// <param name="type">The fully qualified type name of the entity.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resolved <see cref="Type" />, or <see langword="null" /> if not found.</returns>
    Task<Type?> GetInstanceTypeAsync(string type, CancellationToken ct = default);

    /// <summary>
    ///     Finds a workflow by its identifier.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The workflow, or <see langword="null" /> if not found.</returns>
    Task<SchemataWorkflow?> FindAsync(long id, CancellationToken ct = default);

    /// <summary>
    ///     Finds the stateful entity instance associated with the given workflow identifier.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity instance, or <see langword="null" /> if not found.</returns>
    Task<IStatefulEntity?> FindInstanceAsync(long id, CancellationToken ct = default);

    /// <summary>
    ///     Gets the stateful entity instance linked to the given workflow.
    /// </summary>
    /// <param name="workflow">The workflow.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity instance, or <see langword="null" /> if not found.</returns>
    Task<IStatefulEntity?> GetInstanceAsync(SchemataWorkflow workflow, CancellationToken ct = default);

    /// <summary>
    ///     Lists all transitions recorded for the given workflow.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of transitions.</returns>
    IAsyncEnumerable<SchemataTransition> ListTransitionsAsync(long id, CancellationToken ct = default);

    /// <summary>
    ///     Creates a new workflow for the given stateful entity instance.
    /// </summary>
    /// <param name="instance">The entity instance to associate with the new workflow.</param>
    /// <param name="principal">The authenticated user creating the workflow.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created workflow, or <see langword="null" /> on failure.</returns>
    Task<SchemataWorkflow?> CreateAsync(
        IStatefulEntity?  instance,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>
    ///     Creates a new workflow record for an existing entity identified by type and identifier.
    /// </summary>
    /// <param name="instance">The CLR type of the entity.</param>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created workflow, or <see langword="null" /> on failure.</returns>
    Task<SchemataWorkflow?> CreateAsync(Type instance, long id, CancellationToken ct = default);

    /// <summary>
    ///     Raises an event on the specified workflow, causing a state transition.
    /// </summary>
    /// <typeparam name="TEvent">The event data type.</typeparam>
    /// <param name="workflow">The workflow to transition.</param>
    /// <param name="event">The event data.</param>
    /// <param name="principal">The authenticated user raising the event.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RaiseAsync<TEvent>(
        SchemataWorkflow? workflow,
        TEvent            @event,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    )
        where TEvent : class, IEvent;

    /// <summary>
    ///     Raises an event on a workflow identified by its identifier.
    /// </summary>
    /// <typeparam name="TEvent">The event data type.</typeparam>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="event">The event data.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RaiseAsync<TEvent>(long id, TEvent @event, CancellationToken ct = default)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Maps a workflow and its state machine graph into a response object.
    /// </summary>
    /// <param name="workflow">The workflow to map.</param>
    /// <param name="options">Workflow configuration options specifying the response types.</param>
    /// <param name="principal">The authenticated user, used for context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The mapped response object, or <see langword="null" /> if the workflow cannot be resolved.</returns>
    Task<object?> MapAsync(
        SchemataWorkflow?       workflow,
        SchemataWorkflowOptions options,
        ClaimsPrincipal?        principal = null,
        CancellationToken       ct        = default
    );
}
