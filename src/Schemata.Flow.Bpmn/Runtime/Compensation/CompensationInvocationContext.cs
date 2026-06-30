using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Runtime payload passed to a BPMN compensation handler invocation.</summary>
public sealed record CompensationInvocationContext
{
    /// <summary>Initializes a new compensation invocation payload.</summary>
    /// <param name="process">The process instance being compensated.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="scope">The token snapshot associated with the compensation invocation.</param>
    /// <param name="bookkeeping">Scope bookkeeping snapshot captured at compensation time.</param>
    public CompensationInvocationContext(
        SchemataProcess                         process,
        ProcessDefinition                       definition,
        TokenSnapshot                           scope,
        IReadOnlyDictionary<string, int>        bookkeeping) {
        Process    = process;
        Definition = definition;
        Scope      = scope;
        Bookkeeping = bookkeeping;
    }

    /// <summary>The process instance being compensated.</summary>
    public SchemataProcess Process { get; init; }

    /// <summary>The active process definition.</summary>
    public ProcessDefinition Definition { get; init; }

    /// <summary>The token snapshot associated with the compensation invocation.</summary>
    public TokenSnapshot Scope { get; init; }

    /// <summary>Scope bookkeeping snapshot captured at compensation time.</summary>
    public IReadOnlyDictionary<string, int> Bookkeeping { get; init; }

    /// <summary>Transitions written by compensation handler invocations.</summary>
    public IList<SchemataProcessTransition> Transitions { get; } = [];
}
