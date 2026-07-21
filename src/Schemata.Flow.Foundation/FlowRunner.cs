using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Executes Flow runtime operations and persists their results.</summary>
public sealed class FlowRunner(
    IProcessRegistry         registry,
    ProcessPersistence       persistence,
    ProcessLifecycleNotifier notifier,
    IServiceProvider         services
) : IFlowRunner
{
    private static readonly ConcurrentDictionary<Type, ISourceWorker?> SourceWorkers = new();

    #region IFlowRunner Members

    public ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) where TState : class, ICanonicalName {
        if (string.IsNullOrEmpty(source.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TState).FullName}' has no canonical name.");
        }

        return StartCoreAsync(definitionName, options, source, source.CanonicalName, ct);
    }

    public ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) {
        return StartCoreAsync<object>(definitionName, options, null, null, ct);
    }

    #endregion

    /// <summary>Starts a process from a resource request.</summary>
    public ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        string?              source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        return StartCoreAsync<object>(definitionName, options, null, source, ct);
    }

    /// <summary>Starts a process from a resource request without a source entity.</summary>
    public ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        return StartCoreAsync<object>(definitionName, options, null, null, ct);
    }

    /// <summary>Starts a process from a resource request and binds a loaded source entity.</summary>
    public ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) where TState : class, ICanonicalName {
        if (string.IsNullOrEmpty(source.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TState).FullName}' has no canonical name.");
        }

        return StartCoreAsync(definitionName, options, source, source.CanonicalName, ct);
    }

    /// <summary>Completes the addressed token on a process.</summary>
    public async ValueTask<ProcessSnapshot> CompleteAsync(
        SchemataProcess  process,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg    = ResolveRegistration(process.DefinitionName);
        var engine = ResolveEngine(reg);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens = await LoadTokensAsync(scope, process.Name!, c);
            var before = WaitingMap(tokens);
            var ctx    = new FlowExecutionContext(scope.UnitOfWork, services);
            snapshot = await engine.AdvanceAsync(reg.Definition, process, tokens, ctx, token, c);
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    /// <summary>Correlates a message to the process.</summary>
    public async ValueTask<ProcessSnapshot> CorrelateAsync(
        SchemataProcess  process,
        string           messageName,
        string?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg   = ResolveRegistration(process.DefinitionName);
        var value = DeserializePayload(payload, reg.MessagePayloadTypes.GetValueOrDefault(messageName));
        return await CorrelateCoreAsync(process, reg, messageName, value, token, ct);
    }

    /// <summary>Correlates a typed message payload to the process.</summary>
    public ValueTask<ProcessSnapshot> CorrelateAsync(
        SchemataProcess  process,
        string           messageName,
        object?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg = ResolveRegistration(process.DefinitionName);
        return CorrelateCoreAsync(process, reg, messageName, payload, token, ct);
    }

    private async ValueTask<ProcessSnapshot> CorrelateCoreAsync(
        SchemataProcess      process,
        ProcessRegistration  reg,
        string               messageName,
        object?              payload,
        string?              token,
        CancellationToken    ct
    ) {
        var engine  = ResolveEngine(reg);
        var message = reg.Definition.Messages.FirstOrDefault(m => m.Name == messageName);
        if (message is null) {
            throw new InvalidArgumentException(
                SchemataResources.PROCESS_MESSAGE_NOT_DEFINED,
                new Dictionary<string, string?> { ["name"] = messageName }
            );
        }

        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens = await LoadTokensAsync(scope, process.Name!, c);
            var ctx    = new FlowExecutionContext(scope.UnitOfWork, services);
            var target = await ResolveTargetAsync(engine, reg.Definition, process, tokens, ctx, message, token, c);
            var before = WaitingMap(tokens);
            snapshot = await engine.TriggerAsync(reg.Definition, process, tokens, ctx, message, payload, target, c);
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    /// <summary>Broadcasts a signal to waiting processes.</summary>
    public async ValueTask ThrowSignalAsync(
        string           signalName,
        string?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        await ThrowSignalCoreAsync(signalName, payload, token, ct, true);
    }

    /// <summary>Broadcasts a signal with a typed payload to waiting processes.</summary>
    public async ValueTask ThrowSignalAsync(
        string           signalName,
        object?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        await ThrowSignalCoreAsync(signalName, payload, token, ct, false);
    }

    private async ValueTask ThrowSignalCoreAsync(
        string           signalName,
        object?          payload,
        string?          token,
        CancellationToken ct,
        bool             deserialize
    ) {
        await foreach (var process in persistence.ListWaitingAsync(services, ct)) {
            var reg = registry.GetRegistration(process.DefinitionName);
            if (reg is null) {
                continue;
            }

            var signal = reg.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
            if (signal is null) {
                continue;
            }

            var engine = services.GetKeyedService<IFlowRuntime>(reg.Engine);
            if (engine is null) {
                continue;
            }

            var value = deserialize && payload is string text
                ? DeserializePayload(text, reg.SignalPayloadTypes.GetValueOrDefault(signalName))
                : payload;
            await TriggerSignalTargetsAsync(reg, engine, process, signal, value, token, ct);
        }
    }

    /// <summary>Terminates a process and cancels its tokens.</summary>
    public async ValueTask<ProcessSnapshot> TerminateAsync(
        SchemataProcess  process,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg = ResolveRegistration(process.DefinitionName);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens      = await LoadTokensAsync(scope, process.Name!, c);
            var before      = WaitingMap(tokens);
            var ctx         = new FlowExecutionContext(scope.UnitOfWork, services);
            var transitions = new List<SchemataProcessTransition>();
            foreach (var item in tokens) {
                var previous = item.WaitingAtName ?? item.StateName;
                item.State         = "Cancelled";
                item.WaitingAtName = null;

                transitions.Add(CancelTransition(process, item, previous, "Terminated", "Terminate", principal));
            }

            process.State = "Terminated";
            snapshot = new() { Process = process, Tokens = tokens, Transitions = transitions };
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await notifier.NotifyTransitionedAsync(snapshot!, ct);
        await notifier.NotifyTerminatedAsync(process, ct);
        return snapshot!;
    }

    /// <summary>Cancels a token and updates its owning process.</summary>
    public async ValueTask<ProcessSnapshot> CancelTokenAsync(
        SchemataProcessToken token,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var process = await persistence.FindAsync(services, $"processes/{token.Process}", ct);
        if (process is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = token.Process }
            );
        }

        var reg = ResolveRegistration(process.DefinitionName);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens = await LoadTokensAsync(scope, process.Name!, c);
            var ctx    = new FlowExecutionContext(scope.UnitOfWork, services);
            var target = tokens.FirstOrDefault(t => t.CanonicalName == token.CanonicalName);
            if (target is null) {
                throw new NotFoundException(
                    SchemataResources.PROCESS_TOKEN_NOT_FOUND,
                    new Dictionary<string, string?> {
                        ["token"] = token.CanonicalName, ["process"] = process.CanonicalName,
                    }
                );
            }

            if (TokenStates.IsTerminal(target.State)) {
                throw new FailedPreconditionException(
                    message: SchemataResources.GetResourceString(SchemataResources.PROCESS_TOKEN_NOT_READY),
                    reason: SchemataResources.PROCESS_TOKEN_NOT_READY);
            }

            var before   = WaitingMap(tokens);
            var previous = target.WaitingAtName ?? target.StateName;
            target.State         = "Cancelled";
            target.WaitingAtName = null;

            var transition = CancelTransition(process, target, previous, "Cancelled", "CancelToken", principal);

            if (tokens.All(t => TokenStates.IsTerminal(t.State))) {
                process.State = "Cancelled";
            }

            snapshot = new() { Process = process, Tokens = tokens, Transitions = [transition] };
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    private async ValueTask<SchemataProcess> StartCoreAsync<TState>(
        string               definitionName,
        StartProcessOptions? options,
        TState?              source,
        string?              sourceName,
        CancellationToken    ct
    ) where TState : class {
        var reg    = ResolveRegistration(definitionName);
        var engine = ResolveEngine(reg);
        var process = NewProcess(definitionName, options);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            await BindStartSourceAsync(scope, reg, process, source, sourceName, c);
            var ctx = new FlowExecutionContext(scope.UnitOfWork, services);
            snapshot = await engine.StartAsync(reg.Definition, process, ctx, c);
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, new Dictionary<string, string?>(), c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await notifier.NotifyStartedAsync(snapshot!, ct);
        await notifier.NotifyTransitionedAsync(snapshot!, ct);
        return process;
    }

    private async Task TriggerSignalTargetsAsync(
        ProcessRegistration reg,
        IFlowRuntime        engine,
        SchemataProcess     process,
        Signal              signal,
        object?             payload,
        string?             token,
        CancellationToken   ct
    ) {
        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens  = await LoadTokensAsync(scope, process.Name!, c);
            var ctx     = new FlowExecutionContext(scope.UnitOfWork, services);
            var targets = await engine.FindTriggerTargetsAsync(reg.Definition, process, tokens, ctx, signal, c);
            foreach (var target in FilterTargets(targets, token)) {
                var before = WaitingMap(tokens);
                var snapshot = await engine.TriggerAsync(reg.Definition, process, tokens, ctx, signal, payload, target, c);
                await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
                await persistence.PersistSnapshotAsync(scope, snapshot, c);
                await notifier.NotifyTransitionedAsync(snapshot, c);
                if (ProcessStates.IsTerminal(snapshot.Process.State)) {
                    await notifier.NotifyTerminatedAsync(snapshot.Process, c);
                }
            }
        }, ct);
    }

    private async ValueTask ExecuteWithNotificationAsync(
        SchemataProcess                                   process,
        Func<FlowPersistenceScope, CancellationToken, Task> action,
        CancellationToken                                ct
    ) {
        try {
            await persistence.ExecuteAsync(services, action, ct);
        } catch (Exception ex) {
            await notifier.NotifyFailedAsync(process, ex, ct);
            throw;
        }
    }

    private async Task RunAdvisorsAsync(
        ProcessRegistration                 reg,
        FlowPersistenceScope                scope,
        FlowExecutionContext                execution,
        ProcessSnapshot                     snapshot,
        IReadOnlyDictionary<string, string?> before,
        CancellationToken                   ct
    ) {
        var tokens = snapshot.Transitions
                             .Select(transition => transition.Token)
                             .Where(token => !string.IsNullOrEmpty(token))
                             .Select(token => token!)
                             .Distinct(StringComparer.Ordinal);
        foreach (var tokenCanonical in tokens) {
            var token = snapshot.Tokens.FirstOrDefault(current => current.CanonicalName == tokenCanonical);
            if (token is null) {
                continue;
            }

            var context = new FlowTransitionContext {
                Definition            = reg.Definition,
                Snapshot              = snapshot,
                Token                 = TokenSnapshotFactory.From(token),
                PreviousWaitingAtName = before.TryGetValue(tokenCanonical, out var waiting) ? waiting : null,
                UnitOfWork            = scope.UnitOfWork,
            };
            await RunSourceAdvisorsAsync(reg, scope, execution, context, ct);
        }

        await FlushTouchedSourcesAsync(scope, execution, snapshot.Process.CanonicalName ?? string.Empty, ct);

        foreach (var transition in snapshot.Transitions) {
            var token = snapshot.Tokens.FirstOrDefault(t => t.CanonicalName == transition.Token);
            if (token is null) {
                continue;
            }

            var context = new FlowTransitionContext {
                Definition            = reg.Definition,
                Snapshot              = snapshot,
                Token                 = TokenSnapshotFactory.From(token),
                PreviousWaitingAtName = transition.Token is not null && before.TryGetValue(transition.Token, out var waiting) ? waiting : null,
                UnitOfWork            = scope.UnitOfWork,
            };

            var advice = new AdviceContext(services);
            await Advisor.For<IFlowTransitionAdvisor>().RunAsync(advice, context, ct);
        }
    }

    private async Task FlushTouchedSourcesAsync(
        FlowPersistenceScope  scope,
        FlowExecutionContext  execution,
        string                process,
        CancellationToken     ct
    ) {
        foreach (var ((sourceType, _), entity) in execution.TouchedSources) {
            var worker = SourceWorkers.GetOrAdd(sourceType, CreateSourceWorker);
            if (worker is not null) {
                await worker.FlushAsync(services, scope.UnitOfWork, entity, process, ct);
            }
        }

        execution.TouchedSources.Clear();
    }

    private async Task RunSourceAdvisorsAsync(
        ProcessRegistration  reg,
        FlowPersistenceScope scope,
        FlowExecutionContext execution,
        FlowTransitionContext context,
        CancellationToken    ct
    ) {
        var process = context.Snapshot.Process.CanonicalName;
        if (string.IsNullOrEmpty(process)) {
            return;
        }

        var token = context.Token.CanonicalName;
        var bindings = new List<SchemataProcessSource>();
        await foreach (var binding in scope.Sources.ListAsync<SchemataProcessSource>(
                           q => q.Where(s => s.Process == process && (s.Token == null || s.Token == token)), ct)) {
            bindings.Add(binding);
        }

        foreach (var binding in bindings) {
            if (!reg.SourceTypes.TryGetValue(binding.Name, out var descriptor)
             || (descriptor.SourceType.FullName ?? descriptor.SourceType.Name) != binding.SourceType) {
                continue;
            }

            var worker = SourceWorkers.GetOrAdd(descriptor.SourceType, CreateSourceWorker);
            if (worker is not null) {
                await worker.AdviseAsync(services, scope.UnitOfWork, execution, context, binding.Source, ct);
            }
        }
    }

    private async Task BindStartSourceAsync<TState>(
        FlowPersistenceScope scope,
        ProcessRegistration  reg,
        SchemataProcess      process,
        TState?              source,
        string?              sourceName,
        CancellationToken    ct
    ) where TState : class {
        if (source is null && string.IsNullOrEmpty(sourceName)) {
            return;
        }

        var (name, type, canonical, stamp) = StartSource(reg, source, sourceName);
        var row = new SchemataProcessSource {
            Process         = process.CanonicalName!,
            Token           = null,
            Name            = name,
            SourceType      = type,
            Source          = canonical,
            SourceTimestamp = stamp,
        };

        await scope.Sources.AddAsync(row, ct);
    }

    private static (string Name, string Type, string Canonical, Guid? Stamp) StartSource<TState>(
        ProcessRegistration reg,
        TState?             source,
        string?             sourceName
    ) where TState : class {
        if (source is ICanonicalName canonicalSource) {
            var type = typeof(TState);
            var candidates = reg.SourceTypes.Values.Where(descriptor => descriptor.SourceType == type).ToList();
            var conventional = type.Name.Underscore().ToLowerInvariant();
            var binding = candidates.FirstOrDefault(descriptor => descriptor.BindingName == conventional);
            if (binding is null) {
                if (candidates.Count != 1) {
                    throw new FailedPreconditionException(
                        SchemataResources.PROCESS_SOURCE_BINDING_AMBIGUOUS,
                        new Dictionary<string, string?> { ["type"] = type.FullName ?? type.Name });
                }

                binding = candidates[0];
            }

            return (
                binding.BindingName,
                type.FullName ?? type.Name,
                canonicalSource.CanonicalName!,
                source is IConcurrency concurrency ? concurrency.Timestamp : null);
        }

        var types = reg.SourceTypes.ToList();
        if (types.Count != 1) {
            throw new FailedPreconditionException(
                message: $"Process '{reg.Name}' binds {types.Count} source types; specify a source name.");
        }

        return (types[0].Key, types[0].Value.SourceType.FullName ?? types[0].Value.SourceType.Name, sourceName!, null);
    }

    private static async ValueTask<string?> ResolveTargetAsync(
        IFlowRuntime                        engine,
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        string?                             requested,
        CancellationToken                   ct
    ) {
        var targets = await engine.FindTriggerTargetsAsync(definition, process, tokens, context, trigger, ct);
        if (!string.IsNullOrEmpty(requested)) {
            if (!targets.Contains(requested, StringComparer.Ordinal)) {
                throw new FailedPreconditionException(
                    SchemataResources.PROCESS_TOKEN_NOT_READY,
                    new Dictionary<string, string?> { ["name"] = requested });
            }

            return requested;
        }

        if (targets.Count == 0) {
            throw new FailedPreconditionException(SchemataResources.PROCESS_TOKEN_NOT_READY);
        }

        if (targets.Count > 1) {
            throw new FailedPreconditionException(SchemataResources.PROCESS_TOKEN_NOT_READY);
        }

        return targets[0];
    }

    private static IReadOnlyList<string> FilterTargets(IReadOnlyList<string> targets, string? requested) {
        if (string.IsNullOrEmpty(requested)) {
            return targets;
        }

        return targets.Contains(requested, StringComparer.Ordinal) ? [requested] : [];
    }

    private static object? DeserializePayload(string? payload, Type? type) {
        if (string.IsNullOrEmpty(payload)) {
            return null;
        }

        return JsonSerializer.Deserialize(payload, type ?? typeof(object));
    }

    private static Dictionary<string, string?> WaitingMap(IEnumerable<SchemataProcessToken> tokens) {
        return tokens.Where(t => !string.IsNullOrEmpty(t.CanonicalName))
                     .ToDictionary(t => t.CanonicalName!, t => t.WaitingAtName, StringComparer.Ordinal);
    }

    private static SchemataProcessTransition CancelTransition(
        SchemataProcess      process,
        SchemataProcessToken token,
        string?              previous,
        string               posterior,
        string               @event,
        ClaimsPrincipal?     principal
    ) {
        return new() {
            Name      = FlowHandlerSupport.NewLeafId(),
            Process   = process.Name,
            Token     = token.CanonicalName,
            Kind      = TransitionKind.Cancel,
            Previous  = previous,
            Posterior = posterior,
            Event     = @event,
            UpdatedBy = FlowHandlerSupport.ResolveUpdatedBy(principal),
        };
    }

    private static async ValueTask<IReadOnlyList<SchemataProcessToken>> LoadTokensAsync(
        FlowPersistenceScope scope,
        string               processName,
        CancellationToken    ct
    ) {
        var list = new List<SchemataProcessToken>();
        await foreach (var token in scope.Tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.Process == processName), ct)) {
            list.Add(token);
        }

        return list;
    }

    private static SchemataProcess NewProcess(string definitionName, StartProcessOptions? options) {
        var leaf = FlowHandlerSupport.NewLeafId();
        return new() {
            Name           = leaf,
            CanonicalName  = $"processes/{leaf}",
            DefinitionName = definitionName,
            DisplayName    = string.IsNullOrWhiteSpace(options?.DisplayName) ? null : options.DisplayName,
            Description    = string.IsNullOrWhiteSpace(options?.Description) ? null : options.Description,
        };
    }

    private ProcessRegistration ResolveRegistration(string definitionName) {
        var registration = registry.GetRegistration(definitionName);
        if (registration is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = definitionName }
            );
        }

        return registration;
    }

    private IFlowRuntime ResolveEngine(ProcessRegistration reg) {
        var engine = services.GetKeyedService<IFlowRuntime>(reg.Engine);
        if (engine is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_RUNTIME_NOT_REGISTERED,
                new Dictionary<string, string?> { ["engine"] = reg.Engine }
            );
        }

        return engine;
    }

    private async ValueTask NotifyTransitionResultAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        await notifier.NotifyTransitionedAsync(snapshot, ct);
        if (ProcessStates.IsTerminal(snapshot.Process.State)) {
            await notifier.NotifyTerminatedAsync(snapshot.Process, ct);
        }
    }

    private static ISourceWorker? CreateSourceWorker(Type type) {
        if (!typeof(ICanonicalName).IsAssignableFrom(type)) {
            return null;
        }

        var worker = Activator.CreateInstance(typeof(SourceWorker<>).MakeGenericType(type));
        return worker as ISourceWorker;
    }

    #region Nested type: ISourceWorker

    private interface ISourceWorker
    {
        Task AdviseAsync(
            IServiceProvider       services,
            IUnitOfWork            uow,
            FlowExecutionContext   execution,
            FlowTransitionContext  context,
            string                 source,
            CancellationToken      ct
        );

        Task FlushAsync(
            IServiceProvider  services,
            IUnitOfWork       uow,
            object            entity,
            string            process,
            CancellationToken ct
        );
    }

    #endregion

    #region Nested type: SourceWorker

    private sealed class SourceWorker<TSource> : ISourceWorker
        where TSource : class, ICanonicalName
    {
        #region ISourceWorker Members

        public async Task AdviseAsync(
            IServiceProvider      services,
            IUnitOfWork           uow,
            FlowExecutionContext  execution,
            FlowTransitionContext context,
            string                source,
            CancellationToken     ct
        ) {
            var repository = services.GetService<IRepository<TSource>>();
            if (repository is null) {
                return;
            }

            TSource? entity = null;
            if (execution.TouchedSources.TryGetValue((typeof(TSource), source), out var touched)) {
                entity = (TSource)touched;
            } else {
                repository.Join(uow);
                using (repository.SuppressQueryOwner()) {
                    entity = await repository.FirstOrDefaultAsync(q => q.Where(e => e.CanonicalName == source), ct);
                }
            }

            if (entity is null) {
                return;
            }

            var advice = new AdviceContext(services);
            advice.Set(new FlowSourceWriteBack(execution));
            await Advisor.For<IFlowSourceAdvisor<TSource>>().RunAsync(advice, context, entity, ct);
        }

        public async Task FlushAsync(
            IServiceProvider  services,
            IUnitOfWork       uow,
            object            entity,
            string            process,
            CancellationToken ct
        ) {
            var source = (TSource)entity;
            var sources = services.GetRequiredService<IRepository<TSource>>();
            var bindings = services.GetRequiredService<IRepository<SchemataProcessSource>>();
            sources.Join(uow);
            bindings.Join(uow);

            var canonical = source.CanonicalName;
            if (string.IsNullOrEmpty(canonical)) {
                return;
            }

            var type = typeof(TSource).FullName ?? typeof(TSource).Name;
            var rows = new List<SchemataProcessSource>();
            await foreach (var row in bindings.ListAsync<SchemataProcessSource>(
                               q => q.Where(binding => binding.Process == process
                                                   && binding.Source == canonical
                                                   && binding.SourceType == type), ct)) {
                rows.Add(row);
            }

            if (source is IConcurrency concurrent) {
                foreach (var row in rows) {
                    if (row.SourceTimestamp is { } expected && concurrent.Timestamp != expected) {
                        throw new FailedPreconditionException(
                            SchemataResources.FLOW_SOURCE_MODIFIED_CONCURRENTLY,
                            new Dictionary<string, string?> { ["name"] = canonical });
                    }
                }
            }

            await sources.UpdateAsync(source, ct);

            if (source is IConcurrency) {
                TSource? persisted;
                using (sources.SuppressQueryOwner()) {
                    persisted = await sources.FirstOrDefaultAsync(q => q.Where(e => e.CanonicalName == canonical), ct);
                }

                if (persisted is not IConcurrency stamped) {
                    return;
                }

                foreach (var row in rows) {
                    row.SourceTimestamp = stamped.Timestamp;
                    await bindings.UpdateAsync(row, ct);
                }
            }
        }

        #endregion
    }

    #endregion
}
