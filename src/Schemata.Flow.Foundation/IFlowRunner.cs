using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Programmatic entry point for driving registered Flow processes: starting instances and
///     advancing, correlating, signalling, terminating, and cancelling tokens on them.
/// </summary>
public interface IFlowRunner
{
    /// <summary>Starts a registered process definition and binds a source entity to the new instance.</summary>
    ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) where TState : class, ICanonicalName;

    /// <summary>Starts a registered process definition without binding a source entity.</summary>
    ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    );

    /// <summary>Starts a process from a resource request.</summary>
    /// <param name="definitionName">The registered process definition name.</param>
    /// <param name="source">The canonical name of the source entity to bind.</param>
    /// <param name="options">Start options applied to the new instance.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The persisted process instance.</returns>
    ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        string?              source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    );

    /// <summary>Starts a process from a resource request without a source entity.</summary>
    /// <param name="definitionName">The registered process definition name.</param>
    /// <param name="options">Start options applied to the new instance.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The persisted process instance.</returns>
    ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    );

    /// <summary>Starts a process from a resource request and binds a loaded source entity.</summary>
    /// <typeparam name="TState">The source entity type.</typeparam>
    /// <param name="definitionName">The registered process definition name.</param>
    /// <param name="source">The source entity to bind.</param>
    /// <param name="options">Start options applied to the new instance.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The persisted process instance.</returns>
    ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) where TState : class, ICanonicalName;

    /// <summary>Completes the addressed token on a process.</summary>
    /// <param name="process">The process instance to advance.</param>
    /// <param name="token">The token to complete; <see langword="null" /> resolves the single ready token.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The post-operation snapshot persisted by the runner.</returns>
    ValueTask<ProcessSnapshot> CompleteAsync(
        SchemataProcess   process,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );

    /// <summary>Correlates a message to the process.</summary>
    /// <param name="process">The process instance to deliver the message to.</param>
    /// <param name="messageName">The message name declared by the process definition.</param>
    /// <param name="payload">The JSON message payload, deserialized against the registered payload type.</param>
    /// <param name="token">The token to trigger; <see langword="null" /> resolves the single ready token.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The post-operation snapshot persisted by the runner.</returns>
    ValueTask<ProcessSnapshot> CorrelateAsync(
        SchemataProcess   process,
        string            messageName,
        string?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );

    /// <summary>Correlates a typed message payload to the process.</summary>
    /// <param name="process">The process instance to deliver the message to.</param>
    /// <param name="messageName">The message name declared by the process definition.</param>
    /// <param name="payload">The already-deserialized message payload.</param>
    /// <param name="token">The token to trigger; <see langword="null" /> resolves the single ready token.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The post-operation snapshot persisted by the runner.</returns>
    ValueTask<ProcessSnapshot> CorrelateAsync(
        SchemataProcess   process,
        string            messageName,
        object?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );

    /// <summary>Broadcasts a signal to waiting processes.</summary>
    /// <param name="signalName">The signal name declared by waiting process definitions.</param>
    /// <param name="payload">The JSON signal payload, deserialized against the registered payload type.</param>
    /// <param name="token">Restricts delivery to a single token; <see langword="null" /> delivers to every waiting target.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask ThrowSignalAsync(
        string            signalName,
        string?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );

    /// <summary>Broadcasts a signal with a typed payload to waiting processes.</summary>
    /// <param name="signalName">The signal name declared by waiting process definitions.</param>
    /// <param name="payload">The already-deserialized signal payload.</param>
    /// <param name="token">Restricts delivery to a single token; <see langword="null" /> delivers to every waiting target.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask ThrowSignalAsync(
        string            signalName,
        object?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );

    /// <summary>Terminates a process and cancels its tokens.</summary>
    /// <param name="process">The process instance to terminate.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The post-operation snapshot persisted by the runner.</returns>
    ValueTask<ProcessSnapshot> TerminateAsync(
        SchemataProcess   process,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );

    /// <summary>Cancels a token and updates its owning process.</summary>
    /// <param name="token">The token to cancel.</param>
    /// <param name="principal">The principal the request runs as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The post-operation snapshot persisted by the runner.</returns>
    ValueTask<ProcessSnapshot> CancelTokenAsync(
        SchemataProcessToken token,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    );
}
