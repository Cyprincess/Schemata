using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>
///     Indexes a process definition's nested sub-process scopes for runtime token routing.
///     Scope keys are the process instance name at the root and sub-process element names below it.
/// </summary>
public sealed record ProcessScopeMap(
    IReadOnlyDictionary<string, List<FlowElement>> ChildrenByScope,
    IReadOnlyDictionary<string, string>            ParentScope,
    IReadOnlyDictionary<string, FlowElement>       ElementsByName,
    IReadOnlyList<FlowElement>                     AllElements)
{
    public static ProcessScopeMap Build(ProcessDefinition definition, SchemataProcess process) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);

        var childrenByScope = new Dictionary<string, List<FlowElement>>(StringComparer.Ordinal) {
            [process.Name!] = definition.Elements.ToList(),
        };
        var parentScope    = new Dictionary<string, string>(StringComparer.Ordinal);
        var elementsByName = definition.Elements.ToDictionary(e => e.Name, StringComparer.Ordinal);

        foreach (var subProcess in definition.Elements.OfType<SubProcess>()) {
            parentScope[subProcess.Name] = process.Name!;
            AddSubProcess(subProcess, process.Name!, childrenByScope, parentScope, elementsByName);
        }

        return new(childrenByScope, parentScope, elementsByName, [..definition.AllElements]);
    }

    public IEnumerable<string> ScopeChain(SchemataProcess process, string? scopeName) {
        ArgumentNullException.ThrowIfNull(process);

        var current = string.IsNullOrEmpty(scopeName) ? process.Name : scopeName;
        while (!string.IsNullOrEmpty(current)) {
            yield return current;
            if (string.Equals(current, process.Name, StringComparison.Ordinal)) {
                yield break;
            }

            current = ParentScope.TryGetValue(current, out var parent) ? parent : process.Name;
        }
    }

    public bool IsInScope(SchemataProcess process, string? tokenScopeName, string parentScopeName) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrEmpty(parentScopeName);

        if (string.IsNullOrEmpty(tokenScopeName)) {
            return false;
        }

        if (string.Equals(parentScopeName, process.Name, StringComparison.Ordinal)) {
            return true;
        }

        var current = tokenScopeName;
        while (!string.IsNullOrEmpty(current)) {
            if (string.Equals(current, parentScopeName, StringComparison.Ordinal)) {
                return true;
            }

            if (!ParentScope.TryGetValue(current, out current)) {
                return false;
            }
        }

        return false;
    }

    public IEnumerable<SchemataProcessToken> ParentScopeTokens(
        SchemataProcess                   process,
        IEnumerable<SchemataProcessToken> working,
        string                            parentScopeName
    ) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentException.ThrowIfNullOrEmpty(parentScopeName);

        foreach (var token in working) {
            if (token.State is null || !TokenStates.Live.Contains(token.State)) {
                continue;
            }

            if (IsInScope(process, token.ScopeName, parentScopeName)) {
                yield return token;
            }
        }
    }

    public IEnumerable<EventSubProcess> EventSubProcessesInScope(string scopeName) {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);

        return ChildrenByScope.TryGetValue(scopeName, out var children)
            ? children.OfType<EventSubProcess>()
            : [];
    }

    private static void AddSubProcess(
        SubProcess                            subProcess,
        string                                parentScopeName,
        Dictionary<string, List<FlowElement>> childrenByScope,
        Dictionary<string, string>            parentScope,
        Dictionary<string, FlowElement>       elementsByName
    ) {
        childrenByScope[subProcess.Name] = subProcess.Children.ToList();
        parentScope[subProcess.Name]     = parentScopeName;

        foreach (var child in subProcess.Children) {
            elementsByName[child.Name] = child;
        }

        foreach (var child in subProcess.Children.OfType<SubProcess>()) {
            AddSubProcess(child, subProcess.Name, childrenByScope, parentScope, elementsByName);
        }
    }
}
