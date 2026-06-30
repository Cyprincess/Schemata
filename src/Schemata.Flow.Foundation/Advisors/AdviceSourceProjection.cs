using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation.Advisors;

/// <summary>Projects Flow lifecycle and business state onto declared source binding members.</summary>
public sealed class AdviceSourceProjection<TSource>(
    IRepository<TSource>               sources,
    IRepository<SchemataProcessSource> bindings,
    IProcessRegistry                    registry
) : IFlowSourceAdvisor<TSource>
    where TSource : class, ICanonicalName
{
    private static readonly ConcurrentDictionary<(string Process, string Binding), byte> ProjectionWarnings = new();

    #region IAdvisor Members

    public int Order => 0;

    #endregion

    #region IAdvisor<FlowTransitionContext,TSource> Members

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        FlowTransitionContext context,
        TSource               source,
        CancellationToken     ct = default
    ) {
        if (context.UnitOfWork is null) {
            return AdviseResult.Continue;
        }

        var process = context.Snapshot.Process.CanonicalName;
        var canonical = source.CanonicalName;
        if (string.IsNullOrEmpty(process) || string.IsNullOrEmpty(canonical)) {
            return AdviseResult.Continue;
        }

        var registration = registry.GetRegistration(context.Snapshot.Process.DefinitionName);
        if (registration is null) {
            return AdviseResult.Continue;
        }

        sources.Join(context.UnitOfWork);
        bindings.Join(context.UnitOfWork);

        var type = typeof(TSource).FullName ?? typeof(TSource).Name;
        var allRows = new List<SchemataProcessSource>();
        await foreach (var row in bindings.ListAsync<SchemataProcessSource>(
                           q => q.Where(s => s.Process == process
                                          && s.Source == canonical
                                          && s.SourceType == type), ct)) {
            allRows.Add(row);
        }

        var scopedRows = allRows.Where(row => row.Token is null || row.Token == context.Token.CanonicalName).ToList();
        if (scopedRows.Count == 0) {
            return AdviseResult.Continue;
        }

        var marked = ctx.TryGet<FlowSourceWriteBack>(out var writeBack);
        if (!marked && source is IConcurrency concurrent) {
            foreach (var row in allRows) {
                if (row.SourceTimestamp is { } expected && concurrent.Timestamp != expected) {
                    throw new FailedPreconditionException(
                        SchemataResources.FLOW_SOURCE_MODIFIED_CONCURRENTLY,
                        new Dictionary<string, string?> { ["name"] = canonical });
                }
            }
        }

        var logger = ctx.ServiceProvider.GetService<ILogger<AdviceSourceProjection<TSource>>>();
        var dirty = false;
        foreach (var row in scopedRows) {
            if (!registration.SourceTypes.TryGetValue(row.Name, out var descriptor)) {
                logger?.LogDebug("Source binding '{BindingName}' is not declared on process '{Process}'.", row.Name, process);
                continue;
            }

            if (descriptor.SourceType.FullName != row.SourceType) {
                logger?.LogDebug("Source binding '{BindingName}' has a mismatched source type.", row.Name);
                continue;
            }

            if (TryGetStateValue(descriptor, registration.Definition, context, row, process, logger, out var state)) {
                dirty |= SetValueIfChanged(descriptor.GetState, descriptor.SetState, source, state);
            }

            if (descriptor.Projection != FlowSourceProjection.None && descriptor.SetLifecycle is not null) {
                dirty |= SetValueIfChanged(
                    descriptor.GetLifecycle,
                    descriptor.SetLifecycle,
                    source,
                    ScopeLifecycle(context, row));
            }
        }

        if (!dirty) {
            return AdviseResult.Continue;
        }

        if (writeBack is not null) {
            writeBack.Touch(source);
            return AdviseResult.Continue;
        }

        await sources.UpdateAsync(source, ct);

        if (source is IConcurrency stamped) {
            foreach (var row in allRows) {
                row.SourceTimestamp = stamped.Timestamp;
                await bindings.UpdateAsync(row, ct);
            }
        }

        return AdviseResult.Continue;
    }

    #endregion

    private static bool TryGetStateValue(
        FlowSourceDescriptor                       descriptor,
        ProcessDefinition                          definition,
        FlowTransitionContext                      context,
        SchemataProcessSource                      row,
        string                                     process,
        ILogger<AdviceSourceProjection<TSource>>? logger,
        out string?                                state
    ) {
        state = null;
        switch (descriptor.Projection) {
            case FlowSourceProjection.None:
                return false;
            case FlowSourceProjection.Lifecycle:
                state = ScopeLifecycle(context, row);
                return true;
            case FlowSourceProjection.Auto when row.Token is null && ProcessStates.IsTerminal(context.Snapshot.Process.State):
                state = context.Snapshot.Process.State;
                return true;
            case FlowSourceProjection.Auto:
            case FlowSourceProjection.BusinessState:
                return TryGetBusinessState(definition, context, row, process, logger, out state);
            default:
                return false;
        }
    }

    private static bool TryGetBusinessState(
        ProcessDefinition                          definition,
        FlowTransitionContext                      context,
        SchemataProcessSource                      row,
        string                                     process,
        ILogger<AdviceSourceProjection<TSource>>? logger,
        out string?                                state
    ) {
        state = null;
        if (row.Token is not null) {
            if (!IsLive(context.Token.Status) || !IsProjectable(definition, context.Token.StateName)) {
                return false;
            }

            state = context.Token.StateName;
            return true;
        }

        if (ProcessStates.IsTerminal(context.Snapshot.Process.State)) {
            return false;
        }

        var active = context.Snapshot.Tokens.Where(token => IsLive(token.State)).ToList();
        if (active.Count > 1) {
            if (ProjectionWarnings.TryAdd((process, row.Name), 0)) {
                logger?.LogWarning(
                    "Source binding '{BindingName}' on process '{Process}' has multiple active tokens and cannot project one business state.",
                    row.Name,
                    process);
            }

            return false;
        }

        if (active.Count != 1 || active[0].CanonicalName != context.Token.CanonicalName) {
            return false;
        }

        if (!IsProjectable(definition, context.Token.StateName)) {
            return false;
        }

        state = context.Token.StateName;
        return true;
    }

    private static bool IsProjectable(ProcessDefinition definition, string name) {
        return definition.AllElements.FirstOrDefault(element => element.Name == name) is Activity and not ProcedureTaskBase;
    }

    private static bool IsLive(string? state) {
        return string.Equals(state, "Active", StringComparison.Ordinal)
            || string.Equals(state, "Waiting", StringComparison.Ordinal);
    }

    private static string? ScopeLifecycle(FlowTransitionContext context, SchemataProcessSource row) {
        return row.Token is null ? context.Snapshot.Process.State : context.Token.Status;
    }

    private static bool SetValueIfChanged(
        Func<object, string?>?   get,
        Action<object, string?>? set,
        object                   source,
        string?                  value
    ) {
        if (set is null || (get is not null && string.Equals(get(source), value, StringComparison.Ordinal))) {
            return false;
        }

        set(source, value);
        return true;
    }
}
