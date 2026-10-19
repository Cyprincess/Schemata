using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Runtime payload passed to a BPMN compensation handler invocation.</summary>
public sealed record CompensationInvocationContext
{
    /// <summary>Initializes a new compensation invocation payload.</summary>
    /// <param name="process">The process instance being compensated.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="token">The token whose compensation scope is being invoked.</param>
    /// <param name="execution">The execution context of the run that fired the compensation.</param>
    /// <param name="tokens">The operation's mutable token set, shared with compensation continuations.</param>
    public CompensationInvocationContext(
        SchemataProcess      process,
        ProcessDefinition    definition,
        SchemataProcessToken token,
        FlowExecutionContext execution,
        List<SchemataProcessToken> tokens) {
        Process     = process;
        Definition  = definition;
        Token       = token;
        Execution   = execution;
        Tokens      = tokens;
        Scope       = TokenSnapshotFactory.From(token);
        Bookkeeping = new Dictionary<string, int>(token.Bookkeeping, StringComparer.Ordinal);
    }

    /// <summary>The process instance being compensated.</summary>
    public SchemataProcess Process { get; init; }

    /// <summary>The active process definition.</summary>
    public ProcessDefinition Definition { get; init; }

    /// <summary>The token whose compensation scope is being invoked.</summary>
    public SchemataProcessToken Token { get; init; }

    /// <summary>The execution context of the run that fired the compensation.</summary>
    public FlowExecutionContext Execution { get; init; }

    /// <summary>The operation's token set, including tokens spawned by compensation.</summary>
    public List<SchemataProcessToken> Tokens { get; init; }

    /// <summary>The token snapshot associated with the compensation invocation.</summary>
    public TokenSnapshot Scope { get; init; }

    /// <summary>Scope bookkeeping snapshot captured at compensation time.</summary>
    public IReadOnlyDictionary<string, int> Bookkeeping { get; init; }

    /// <summary>Transitions written by compensation handler invocations.</summary>
    public IList<SchemataProcessTransition> Transitions { get; } = [];
}
