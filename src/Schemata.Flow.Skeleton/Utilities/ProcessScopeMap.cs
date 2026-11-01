using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>Indexes structural scopes and resolves their token activation lineage.</summary>
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
            [string.Empty] = definition.Elements.ToList(),
        };
        var parentScope = new Dictionary<string, string>(StringComparer.Ordinal);
        var elementsByName = definition.Elements.ToDictionary(e => e.Name, StringComparer.Ordinal);
        foreach (var subProcess in definition.Elements.OfType<SubProcess>()) {
            AddSubProcess(subProcess, string.Empty, childrenByScope, parentScope, elementsByName);
        }
        return new(childrenByScope, parentScope, elementsByName, [..definition.AllElements]);
    }

    public IEnumerable<string> ScopeChain(SchemataProcess process, string? scopeName) {
        ArgumentNullException.ThrowIfNull(process);
        var current = scopeName ?? string.Empty;
        while (true) {
            yield return current;
            if (current.Length == 0) {
                yield break;
            }
            current = ParentScope.TryGetValue(current, out var parent) ? parent : string.Empty;
        }
    }

    public bool IsInScope(SchemataProcess process, string? tokenScopeName, string parentScopeName) {
        return ScopeChain(process, tokenScopeName).Contains(parentScopeName, StringComparer.Ordinal);
    }

    public static SchemataProcessToken? ScopeOwner(SchemataProcessToken token, IReadOnlyList<SchemataProcessToken> working) {
        if (string.IsNullOrEmpty(token.ScopeName)) {
            return null;
        }
        var current = token;
        while (current.Spawner is { } spawner) {
            var parent = working.FirstOrDefault(candidate => candidate.CanonicalName == spawner);
            if (parent is null) {
                return null;
            }
            if (parent.ScopeName != token.ScopeName) {
                return parent;
            }
            current = parent;
        }
        return null;
    }

    public static bool SameActivation(SchemataProcessToken token, SchemataProcessToken other, IReadOnlyList<SchemataProcessToken> working) {
        return token.ScopeName == other.ScopeName
            && ScopeOwner(token, working)?.CanonicalName == ScopeOwner(other, working)?.CanonicalName;
    }

    public static bool IsDescendant(SchemataProcessToken token, SchemataProcessToken owner, IReadOnlyList<SchemataProcessToken> working) {
        var current = token;
        while (current.Spawner is { } spawner) {
            if (spawner == owner.CanonicalName) {
                return true;
            }
            var parent = working.FirstOrDefault(candidate => candidate.CanonicalName == spawner);
            if (parent is null) {
                return false;
            }
            current = parent;
        }
        return false;
    }

    public IEnumerable<SchemataProcessToken> ParentScopeTokens(
        SchemataProcess process,
        IReadOnlyList<SchemataProcessToken> working,
        string parentScopeName,
        SchemataProcessToken addressed
    ) {
        var owner = addressed;
        while (owner.ScopeName != parentScopeName && ScopeOwner(owner, working) is { } parent) {
            owner = parent;
        }
        var activation = ScopeOwner(owner, working);
        foreach (var token in working) {
            if (token.State is { } state && TokenStates.Live.Contains(state)
             && IsInScope(process, token.ScopeName, parentScopeName)
             && (activation is null || IsDescendant(token, activation, working))) {
                yield return token;
            }
        }
    }

    public IEnumerable<EventSubProcess> EventSubProcessesInScope(string scopeName) {
        return ChildrenByScope.TryGetValue(scopeName, out var children) ? children.OfType<EventSubProcess>() : [];
    }

    private static void AddSubProcess(
        SubProcess subProcess,
        string parentScopeName,
        Dictionary<string, List<FlowElement>> childrenByScope,
        Dictionary<string, string> parentScope,
        Dictionary<string, FlowElement> elementsByName
    ) {
        childrenByScope[subProcess.Name] = subProcess.Children.ToList();
        parentScope[subProcess.Name] = parentScopeName;
        foreach (var child in subProcess.Children) {
            elementsByName[child.Name] = child;
        }
        foreach (var child in subProcess.Children.OfType<SubProcess>()) {
            AddSubProcess(child, subProcess.Name, childrenByScope, parentScope, elementsByName);
        }
    }
}
