using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Foundation.Handlers;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using CompleteProcessRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using CorrelateProcessRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;
using ThrowProcessSignalRequest = Schemata.Flow.Foundation.Commands.ThrowSignalRequest;

namespace Schemata.Flow.Foundation;

/// <summary>Facade that converts Flow operations into request-handler invocations.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
public sealed class FlowRunner<TProcess, TToken>(IServiceProvider services) : IFlowRunner<TProcess, TToken>
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
{
    #region IFlowRunner Members

    public ValueTask<TProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) where TState : class, ICanonicalName {
        if (string.IsNullOrEmpty(source.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TState).FullName}' has no canonical name.");
        }

        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        var inner = new StartProcessRequest(
            definitionName,
            source,
            typeof(TState),
            source.CanonicalName,
            options,
            Principal: null);
        return StartResultAsync(dispatcher, inner, null, ct);
    }

    public ValueTask<TProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) {
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        var inner = new StartProcessRequest(
            definitionName,
            Source: null,
            SourceType: null,
            SourceCanonicalName: null,
            options,
            Principal: null);
        return StartResultAsync(dispatcher, inner, null, ct);
    }

    public ValueTask<ProcessSnapshot> CompleteAsync(
        TProcess          process,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var canonicalName = RequireCanonicalName(process.CanonicalName, nameof(process));
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, CompleteProcessRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.Complete, canonicalName, new(canonicalName, token, principal), principal), ct));
    }

    public ValueTask<ProcessSnapshot> CorrelateAsync(
        TProcess          process,
        string            messageName,
        string?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var canonicalName = RequireCanonicalName(process.CanonicalName, nameof(process));
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, CorrelateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.Correlate, canonicalName, new(canonicalName, messageName, payload, token, principal), principal), ct));
    }

    public ValueTask<ProcessSnapshot> CorrelateAsync(
        TProcess          process,
        string            messageName,
        object?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var canonicalName = RequireCanonicalName(process.CanonicalName, nameof(process));
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, CorrelateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.Correlate,
                canonicalName,
                new(canonicalName, messageName, FlowPayload.Preserve(payload), token, principal),
                principal), ct));
    }

    public ValueTask<IReadOnlyList<SignalDeliveryResult>> ThrowSignalAsync(
        string            signalName,
        string?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, ThrowProcessSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(
            new(FlowOperations.Signal, null, new(signalName, payload, token, principal), principal), ct));
    }

    public ValueTask<IReadOnlyList<SignalDeliveryResult>> ThrowSignalAsync(
        string            signalName,
        object?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, ThrowProcessSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(
            new(FlowOperations.Signal,
                null,
                new(signalName, FlowPayload.Preserve(payload), token, principal),
                principal), ct));
    }

    public ValueTask<ProcessSnapshot> TerminateAsync(
        TProcess          process,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var canonicalName = RequireCanonicalName(process.CanonicalName, nameof(process));
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.Terminate, canonicalName, new(canonicalName, principal), principal), ct));
    }

    public ValueTask<ProcessSnapshot> CancelTokenAsync(
        TToken            token,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var tokenCanonicalName = RequireCanonicalName(token.CanonicalName, nameof(token));
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.Cancel, tokenCanonicalName, new($"processes/{token.Process}", tokenCanonicalName, principal), principal), ct));
    }

    #endregion

    /// <summary>Starts a process from a resource request.</summary>
    public ValueTask<TProcess> StartAsync(
        string               definitionName,
        string?              source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        var inner = new StartProcessRequest(
            definitionName,
            Source: null,
            SourceType: null,
            SourceCanonicalName: source,
            options,
            principal);
        return StartResultAsync(dispatcher, inner, principal, ct);
    }

    /// <summary>Starts a process from a resource request without a source entity.</summary>
    public ValueTask<TProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        var inner = new StartProcessRequest(
            definitionName,
            Source: null,
            SourceType: null,
            SourceCanonicalName: null,
            options,
            principal);
        return StartResultAsync(dispatcher, inner, principal, ct);
    }

    /// <summary>Starts a process from a resource request and binds a loaded source entity.</summary>
    public ValueTask<TProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) where TState : class, ICanonicalName {
        if (string.IsNullOrEmpty(source.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TState).FullName}' has no canonical name.");
        }

        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        var inner = new StartProcessRequest(
            definitionName,
            source,
            typeof(TState),
            source.CanonicalName,
            options,
            principal);
        return StartResultAsync(dispatcher, inner, principal, ct);
    }

    /// <summary>Triggers an addressed internal event through the process request handler.</summary>
    public ValueTask<ProcessSnapshot> RunEventAsync(
        string            processName,
        string?           tokenName,
        IEventDefinition  trigger,
        object?           payload,
        CancellationToken ct
    ) {
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();
        return new(dispatcher.SendAsync<ResourceMethodRequest<TProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.RunEvent, processName, new(processName, tokenName, trigger, payload), null), ct));
    }

    private static async ValueTask<TProcess> StartResultAsync(
        IRequestDispatcher  dispatcher,
        StartProcessRequest inner,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        var started = await dispatcher.SendAsync<ResourceMethodRequest<TProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(
            new(FlowOperations.Start, null, inner, principal), ct);
        return (TProcess)started;
    }

    private static string RequireCanonicalName(string? canonicalName, string parameterName) {
        return canonicalName
            ?? throw new ArgumentException("The resource must have a canonical name.", parameterName);
    }
}
