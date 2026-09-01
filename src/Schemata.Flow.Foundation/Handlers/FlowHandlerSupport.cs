using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class FlowHandlerSupport(
    IProcessRegistry              registry,
    ProcessPersistence            persistence,
    ProcessLifecycleNotifier      notifier,
    IServiceProvider              services,
    IServiceScopeFactory          scopes,
    IOptions<SchemataFlowOptions> options
)
{
    private static readonly ConcurrentDictionary<Type, ISourceWorker?> SourceWorkers = new();

    internal ProcessPersistence Persistence => persistence;

    internal ProcessLifecycleNotifier Notifier => notifier;

    internal IServiceProvider Services => services;

    internal IServiceScopeFactory Scopes => scopes;

    internal int SignalBroadcastConcurrency => Math.Max(1, options.Value.SignalBroadcastConcurrency);

    internal async ValueTask<SchemataProcess> LoadProcessAsync(string canonicalName, CancellationToken ct) {
        var process = await persistence.FindAsync(services, canonicalName, ct);
        if (process is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = canonicalName }
            );
        }

        return process;
    }

    internal ProcessRegistration ResolveRegistration(string definitionName) {
        var registration = registry.GetRegistration(definitionName);
        if (registration is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = definitionName }
            );
        }

        return registration;
    }

    internal ProcessRegistration? FindRegistration(string definitionName) {
        return registry.GetRegistration(definitionName);
    }

    internal IFlowRuntime ResolveEngine(ProcessRegistration registration) {
        var engine = services.GetKeyedService<IFlowRuntime>(registration.Engine);
        if (engine is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_RUNTIME_NOT_REGISTERED,
                new Dictionary<string, string?> { ["engine"] = registration.Engine }
            );
        }

        return engine;
    }

    internal async ValueTask ExecuteWithNotificationAsync(
        SchemataProcess                                     process,
        Func<FlowPersistenceScope, CancellationToken, Task> action,
        CancellationToken                                  ct
    ) {
        try {
            await persistence.ExecuteAsync(services, action, ct);
        } catch (Exception ex) {
            await notifier.NotifyFailedAsync(process, ex, ct);
            throw;
        }
    }

    internal async ValueTask<ProcessSnapshot> TriggerAddressedAsync(
        SchemataProcess     process,
        ProcessRegistration registration,
        IFlowRuntime        engine,
        IEventDefinition    trigger,
        object?             payload,
        string?             token,
        bool                resolveTarget,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens    = await LoadTokensAsync(scope, process.Name!, current);
            var context   = await CreateExecutionContextAsync(scope, process, principal, current);
            var tokenName = resolveTarget
                ? await ResolveTargetAsync(
                    engine, registration.Definition, process, tokens, context, trigger, token, current)
                : token;
            var before = WaitingMap(tokens);
            snapshot = await engine.TriggerAsync(
                registration.Definition, process, tokens, context, trigger, payload, tokenName, current);
            EnsureCatchesHaveHandlers(registration.Definition, snapshot);
            await RunAdvisorsAsync(registration, scope, context, snapshot, before, current);
            await persistence.PersistSnapshotAsync(scope, snapshot, current);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    internal async Task RunAdvisorsAsync(
        ProcessRegistration                  registration,
        FlowPersistenceScope                 scope,
        FlowExecutionContext                 execution,
        ProcessSnapshot                      snapshot,
        IReadOnlyDictionary<string, string?> before,
        CancellationToken                    ct
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
                Definition            = registration.Definition,
                Snapshot              = snapshot,
                Token                 = TokenSnapshotFactory.From(token),
                PreviousWaitingAtName = before.TryGetValue(tokenCanonical, out var waiting) ? waiting : null,
                UnitOfWork            = scope.UnitOfWork,
                Principal             = execution.Principal,
            };
            await RunSourceAdvisorsAsync(registration, scope, execution, context, ct);
        }

        await FlushTouchedSourcesAsync(scope, execution, snapshot.Process.CanonicalName ?? string.Empty, ct);

        var handlers = services.GetServices<IFlowCatchHandler>().ToList();
        foreach (var transition in snapshot.Transitions) {
            var token = snapshot.Tokens.FirstOrDefault(current => current.CanonicalName == transition.Token);
            if (token is null) {
                continue;
            }

            var context = new FlowTransitionContext {
                Definition            = registration.Definition,
                Snapshot              = snapshot,
                Token                 = TokenSnapshotFactory.From(token),
                PreviousWaitingAtName = transition.Token is not null
                                     && before.TryGetValue(transition.Token, out var waiting)
                    ? waiting
                    : null,
                UnitOfWork = scope.UnitOfWork,
                Principal  = execution.Principal,
            };

            var ctx = AdviceContext.Current ?? new AdviceContext(services);
            await Advisor.For<IFlowTransitionAdvisor>().RunAsync(ctx, context, ct);
            foreach (var handler in handlers) {
                await handler.ArmAsync(context, ct);
            }
        }
    }

    internal void EnsureCatchesHaveHandlers(ProcessDefinition definition, ProcessSnapshot snapshot) {
        var handlers = services.GetServices<IFlowCatchHandler>().ToList();
        var changed = snapshot.Transitions
                              .Select(transition => transition.Token)
                              .Where(token => !string.IsNullOrEmpty(token))
                              .Select(token => token!)
                              .Distinct(StringComparer.Ordinal);

        foreach (var tokenName in changed) {
            var token = snapshot.Tokens.FirstOrDefault(current => current.CanonicalName == tokenName);
            if (token is null) {
                continue;
            }

            foreach (var catchEvent in ResolveExternalCatches(definition, token)) {
                FlowCatchKind? kind = catchEvent.Definition switch {
                    Message         => FlowCatchKind.Message,
                    Signal          => FlowCatchKind.Signal,
                    TimerDefinition => FlowCatchKind.Timer,
                    _               => null,
                };

                if (kind is null || handlers.Any(handler => handler.Handles(kind.Value))) {
                    continue;
                }

                throw new FailedPreconditionException(
                    message: $"Flow catch '{catchEvent.Name}' waits on a {kind} event, but no registered "
                           + $"{nameof(IFlowCatchHandler)} delivers that kind; the token would wait forever.");
            }
        }
    }

    internal async Task BindStartSourceAsync(
        FlowPersistenceScope scope,
        ProcessRegistration  registration,
        SchemataProcess      process,
        object?              source,
        Type?                sourceType,
        string?              sourceName,
        CancellationToken    ct
    ) {
        if (source is null && string.IsNullOrEmpty(sourceName)) {
            return;
        }

        var (name, type, canonical, stamp) = StartSource(registration, source, sourceType, sourceName);
        await scope.Sources.AddAsync(new SchemataProcessSource {
            Process         = process.CanonicalName!,
            Token           = null,
            Name            = name,
            SourceType      = type,
            Source          = canonical,
            SourceTimestamp = stamp,
        }, ct);
    }

    internal static (string Name, string Type, string Canonical, Guid? Stamp) StartSource(
        ProcessRegistration registration,
        object?             source,
        Type?               sourceType,
        string?             sourceName
    ) {
        if (source is ICanonicalName canonicalSource) {
            if (sourceType is null) {
                throw new FailedPreconditionException(
                    SchemataResources.PROCESS_SOURCE_BINDING_AMBIGUOUS,
                    new Dictionary<string, string?> { ["type"] = null });
            }

            var candidates = registration.SourceTypes.Values
                                         .Where(descriptor => descriptor.SourceType == sourceType)
                                         .ToList();
            var conventional = FlowSourceDescriptor.DefaultBindingName(sourceType);
            var binding = candidates.FirstOrDefault(descriptor => descriptor.BindingName == conventional);
            if (binding is null) {
                if (candidates.Count != 1) {
                    throw new FailedPreconditionException(
                        SchemataResources.PROCESS_SOURCE_BINDING_AMBIGUOUS,
                        new Dictionary<string, string?> { ["type"] = FlowSourceTypeNames.ToName(sourceType) });
                }

                binding = candidates[0];
            }

            return (
                binding.BindingName,
                FlowSourceTypeNames.ToName(sourceType),
                canonicalSource.CanonicalName!,
                source is IConcurrency concurrency ? concurrency.Timestamp : null);
        }

        var types = registration.SourceTypes.ToList();
        if (types.Count != 1) {
            throw new FailedPreconditionException(
                message: $"Process '{registration.Name}' binds {types.Count} source types; specify a source name.");
        }

        return (types[0].Key, FlowSourceTypeNames.ToName(types[0].Value.SourceType), sourceName!, null);
    }

    internal static object? DeserializePayload(object? payload, Type? type) {
        if (payload is TypedPayload typed) {
            return typed.Value;
        }

        if (payload is not string text) {
            return payload;
        }

        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        if (type is null) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAYLOAD);
        }

        return JsonSerializer.Deserialize(text, type, SchemataJson.Default);
    }

    internal static object? PreserveTypedPayload(object? payload) {
        return payload is string ? new TypedPayload(payload) : payload;
    }

    internal static Dictionary<string, string?> WaitingMap(IEnumerable<SchemataProcessToken> tokens) {
        return tokens.Where(token => !string.IsNullOrEmpty(token.CanonicalName))
                     .ToDictionary(
                          token => token.CanonicalName!, token => token.WaitingAtName, StringComparer.Ordinal);
    }

    internal static SchemataProcessTransition CancelTransition(
        SchemataProcess      process,
        SchemataProcessToken token,
        string?              previous,
        string               posterior,
        string               @event,
        ClaimsPrincipal?     principal
    ) {
        return new() {
            Name      = NewLeafId(),
            Process   = process.Name,
            Token     = token.CanonicalName,
            Kind      = TransitionKind.Cancel,
            Previous  = previous,
            Posterior = posterior,
            Event     = @event,
            UpdatedBy = ResolveUpdatedBy(principal),
        };
    }

    internal static IReadOnlyList<string> FilterTargets(IReadOnlyList<string> targets, string? requested) {
        if (string.IsNullOrEmpty(requested)) {
            return targets;
        }

        return targets.Contains(requested, StringComparer.Ordinal) ? [requested] : [];
    }

    internal static async ValueTask<IReadOnlyList<SchemataProcessToken>> LoadTokensAsync(
        FlowPersistenceScope scope,
        string               processName,
        CancellationToken    ct
    ) {
        var list = new List<SchemataProcessToken>();
        await foreach (var token in scope.Tokens.ListAsync<SchemataProcessToken>(
                           query => query.Where(current => current.Process == processName), ct)) {
            list.Add(token);
        }

        return list;
    }

    internal async ValueTask<FlowExecutionContext> CreateExecutionContextAsync(
        FlowPersistenceScope scope,
        SchemataProcess      process,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var bindings = new List<ProcessCompensationBinding>();
        if (!string.IsNullOrEmpty(process.CanonicalName)) {
            await foreach (var binding in scope.Compensations.ListAsync<SchemataProcessCompensation>(
                               query => query.Where(row => row.Process == process.CanonicalName)
                                             .OrderBy(row => row.ScopeOwnerCanonicalName)
                                             .ThenBy(row => row.RegistrationOrder)
                                             .ThenBy(row => row.ActivityName), ct)) {
                bindings.Add(new(
                    binding.ScopeOwnerCanonicalName,
                    binding.ActivityName,
                    binding.RegistrationOrder));
            }
        }

        return new(scope.UnitOfWork, services) {
            LoadedCompensationBindings = bindings,
            Principal                  = principal,
            SourceReadGuard            = FlowSourceReadScope.Enter,
        };
    }

    internal static SchemataProcess NewProcess(string definitionName, StartProcessOptions? startOptions) {
        var leaf = NewLeafId();
        return new() {
            Name           = leaf,
            CanonicalName  = $"processes/{leaf}",
            DefinitionName = definitionName,
            DisplayName    = string.IsNullOrWhiteSpace(startOptions?.DisplayName) ? null : startOptions.DisplayName,
            Description    = string.IsNullOrWhiteSpace(startOptions?.Description) ? null : startOptions.Description,
            IdempotencyKey = string.IsNullOrWhiteSpace(startOptions?.IdempotencyKey) ? null : startOptions.IdempotencyKey,
        };
    }

    internal async ValueTask NotifyTransitionResultAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        await notifier.NotifyTransitionedAsync(snapshot, ct);
        if (ProcessStates.IsTerminal(snapshot.Process.State)) {
            await notifier.NotifyTerminatedAsync(snapshot.Process, ct);
        }
    }

    internal static string? ResolveUpdatedBy(ClaimsPrincipal? principal) {
        if (principal is null) {
            return null;
        }

        var subject = principal.FindFirst(SchemataConstants.IdentityClaims.Subject)?.Value;
        if (!string.IsNullOrWhiteSpace(subject)) {
            return $"users/{subject}";
        }

        return principal.Identity?.Name;
    }

    internal static string NewLeafId() {
        return Identifiers.NewUid().ToString("n");
    }

    private static IEnumerable<FlowEvent> ResolveExternalCatches(
        ProcessDefinition    definition,
        SchemataProcessToken token
    ) {
        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            var waiting = definition.AllElements.FirstOrDefault(element => element.Name == token.WaitingAtName);
            if (waiting is FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent) {
                yield return catchEvent;
                yield break;
            }

            if (waiting is EventBasedGateway gateway) {
                foreach (var flow in definition.AllFlows.Where(flow => flow.Source == gateway)) {
                    if (flow.Target is FlowEvent { Position: EventPosition.IntermediateCatch } outgoingCatch) {
                        yield return outgoingCatch;
                    }
                }
            }

            yield break;
        }

        if (!string.Equals(token.State, "Active", StringComparison.Ordinal)
         || definition.AllElements.FirstOrDefault(element => element.Name == token.StateName) is not Activity host) {
            yield break;
        }

        foreach (var catchEvent in definition.AllElements.OfType<FlowEvent>()) {
            if (catchEvent.Position == EventPosition.Boundary && ReferenceEquals(catchEvent.AttachedTo, host)) {
                yield return catchEvent;
            }
        }
    }

    private async Task FlushTouchedSourcesAsync(
        FlowPersistenceScope scope,
        FlowExecutionContext execution,
        string               process,
        CancellationToken    ct
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
        ProcessRegistration  registration,
        FlowPersistenceScope scope,
        FlowExecutionContext execution,
        FlowTransitionContext context,
        CancellationToken    ct
    ) {
        var process = context.Snapshot.Process.CanonicalName;
        if (string.IsNullOrEmpty(process)) {
            return;
        }

        var token    = context.Token.CanonicalName;
        var bindings = new List<SchemataProcessSource>();
        await foreach (var binding in scope.Sources.ListAsync<SchemataProcessSource>(
                           query => query.Where(source => source.Process == process
                                                      && (source.Token == null || source.Token == token)), ct)) {
            bindings.Add(binding);
        }

        foreach (var binding in bindings) {
            if (!registration.SourceTypes.TryGetValue(binding.Name, out var descriptor)
             || FlowSourceTypeNames.ToName(descriptor.SourceType) != binding.SourceType) {
                continue;
            }

            var worker = SourceWorkers.GetOrAdd(descriptor.SourceType, CreateSourceWorker);
            if (worker is not null) {
                await worker.AdviseAsync(services, scope.UnitOfWork, execution, context, binding.Source, ct);
            }
        }
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

        if (targets.Count != 1) {
            throw new FailedPreconditionException(SchemataResources.PROCESS_TOKEN_NOT_READY);
        }

        return targets[0];
    }

    private static ISourceWorker? CreateSourceWorker(Type type) {
        if (!typeof(ICanonicalName).IsAssignableFrom(type)) {
            return null;
        }

        return Activator.CreateInstance(typeof(SourceWorker<>).MakeGenericType(type)) as ISourceWorker;
    }

    private sealed record TypedPayload(object Value);

    private interface ISourceWorker
    {
        Task AdviseAsync(
            IServiceProvider      provider,
            IUnitOfWork           unitOfWork,
            FlowExecutionContext  execution,
            FlowTransitionContext context,
            string                source,
            CancellationToken     ct
        );

        Task FlushAsync(
            IServiceProvider provider,
            IUnitOfWork      unitOfWork,
            object           entity,
            string           process,
            CancellationToken ct
        );
    }

    private sealed class SourceWorker<TSource> : ISourceWorker
        where TSource : class, ICanonicalName
    {
        public async Task AdviseAsync(
            IServiceProvider      provider,
            IUnitOfWork           unitOfWork,
            FlowExecutionContext  execution,
            FlowTransitionContext context,
            string                source,
            CancellationToken     ct
        ) {
            var repository = provider.GetRequiredService<IRepository<TSource>>();

            TSource? entity;
            if (execution.TouchedSources.TryGetValue((typeof(TSource), source), out var touched)) {
                entity = (TSource)touched;
            } else {
                repository.Join(unitOfWork);
                using (FlowSourceReadScope.Enter(repository)) {
                    entity = await repository.FirstOrDefaultAsync(
                        query => query.Where(current => current.CanonicalName == source), ct);
                }
            }

            if (entity is null) {
                return;
            }

            var advice = AdviceContext.Current ?? new AdviceContext(provider);
            advice.Set(new FlowSourceWriteBack(execution));
            await Advisor.For<IFlowSourceAdvisor<TSource>>().RunAsync(advice, context, entity, ct);
        }

        public async Task FlushAsync(
            IServiceProvider provider,
            IUnitOfWork      unitOfWork,
            object           entity,
            string           process,
            CancellationToken ct
        ) {
            var source   = (TSource)entity;
            var sources  = provider.GetRequiredService<IRepository<TSource>>();
            var bindings = provider.GetRequiredService<IRepository<SchemataProcessSource>>();
            sources.Join(unitOfWork);
            bindings.Join(unitOfWork);

            var canonical = source.CanonicalName;
            if (string.IsNullOrEmpty(canonical)) {
                return;
            }

            var type = FlowSourceTypeNames.ToName(typeof(TSource));
            var rows = new List<SchemataProcessSource>();
            await foreach (var row in bindings.ListAsync<SchemataProcessSource>(
                               query => query.Where(binding => binding.Process == process
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

            if (source is not IConcurrency) {
                return;
            }

            TSource? persisted;
            using (FlowSourceReadScope.Enter(sources)) {
                persisted = await sources.FirstOrDefaultAsync(
                    query => query.Where(current => current.CanonicalName == canonical), ct);
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
}
