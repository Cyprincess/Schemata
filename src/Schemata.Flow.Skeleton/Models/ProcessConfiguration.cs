using System;
using Schemata.Abstractions;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Registration metadata for a <see cref="ProcessDefinition" /> held by <see cref="Runtime.IProcessRegistry" />.</summary>
public sealed class ProcessConfiguration
{
    /// <summary>Process definition name (the value used in canonical names and lookup).</summary>
    public string Name { get; set; } = null!;

    /// <summary>Engine that executes this definition.  Defaults to the state-machine engine.</summary>
    public string Engine { get; set; } = SchemataConstants.FlowEngines.StateMachine;

    /// <summary>CLR type of the <see cref="ProcessDefinition" /> subclass.</summary>
    public Type? DefinitionType { get; set; }

    /// <summary>The default expression language for this process's string conditions.</summary>
    public string? Language { get; set; }
}
