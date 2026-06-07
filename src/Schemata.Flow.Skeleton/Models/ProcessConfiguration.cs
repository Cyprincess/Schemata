using System;
using System.Collections.Generic;
using Humanizer;
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

    /// <summary>Optional CLR entity type owning the process instance lifecycle.</summary>
    public Type? EntityType { get; set; }

    /// <summary>Map of variable name to CLR type for typed variable bindings.</summary>
    public Dictionary<string, Type> VariableTypes { get; set; } = new();

    /// <summary>Map of message name to CLR payload type for typed correlation.</summary>
    public Dictionary<string, Type> MessageTypes { get; set; } = new();

    /// <summary>Map of signal name to CLR payload type for typed broadcast.</summary>
    public Dictionary<string, Type> SignalTypes { get; set; } = new();

    /// <summary>When <c>true</c>, process operations enforce authorization.</summary>
    public bool RequiresAuthorization { get; set; }

    /// <summary>Registers a typed variable binding under the supplied name (or the snake-cased type name).</summary>
    public ProcessConfiguration WithVariable<T>(string? name = null) {
        var key = name ?? typeof(T).Name.Underscore().ToLowerInvariant();
        VariableTypes[key] = typeof(T);
        return this;
    }

    /// <summary>Registers a typed message binding under the supplied name (or the snake-cased type name).</summary>
    public ProcessConfiguration WithMessage<T>(string? name = null) {
        var key = name ?? typeof(T).Name.Underscore().ToLowerInvariant();
        MessageTypes[key] = typeof(T);
        return this;
    }

    /// <summary>Registers a typed signal binding under the supplied name (or the snake-cased type name).</summary>
    public ProcessConfiguration WithSignal<T>(string? name = null) {
        var key = name ?? typeof(T).Name.Underscore().ToLowerInvariant();
        SignalTypes[key] = typeof(T);
        return this;
    }

    /// <summary>Enables authorization checks on process operations.</summary>
    public ProcessConfiguration WithAuthorization() {
        RequiresAuthorization = true;
        return this;
    }
}
