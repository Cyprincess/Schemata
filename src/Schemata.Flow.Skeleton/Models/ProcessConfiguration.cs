using System;
using System.Collections.Generic;
using Humanizer;
using Schemata.Abstractions;

namespace Schemata.Flow.Skeleton.Models;

public sealed class ProcessConfiguration
{
    public string Name { get; set; } = null!;

    public string Engine { get; set; } = SchemataConstants.FlowEngines.StateMachine;

    public Type? DefinitionType { get; set; }

    public Type? EntityType { get; set; }

    public Dictionary<string, Type> VariableTypes { get; set; } = new();

    public Dictionary<string, Type> MessageTypes { get; set; } = new();

    public Dictionary<string, Type> SignalTypes { get; set; } = new();

    public bool RequiresAuthorization { get; set; }

    public ProcessConfiguration WithVariable<T>(string? name = null) {
        var key = name ?? typeof(T).Name.Underscore().ToLowerInvariant();
        VariableTypes[key] = typeof(T);
        return this;
    }

    public ProcessConfiguration WithMessage<T>(string? name = null) {
        var key = name ?? typeof(T).Name.Underscore().ToLowerInvariant();
        MessageTypes[key] = typeof(T);
        return this;
    }

    public ProcessConfiguration WithSignal<T>(string? name = null) {
        var key = name ?? typeof(T).Name.Underscore().ToLowerInvariant();
        SignalTypes[key] = typeof(T);
        return this;
    }

    public ProcessConfiguration WithAuthorization() {
        RequiresAuthorization = true;
        return this;
    }
}
