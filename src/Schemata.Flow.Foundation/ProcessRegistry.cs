using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;

namespace Schemata.Flow.Foundation;

public sealed class ProcessRegistry : IProcessRegistry
{
    private readonly Dictionary<string, ProcessRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider                        _services;

    public ProcessRegistry(IServiceProvider services) { _services = services; }

    #region IProcessRegistry Members

    public ValueTask RegisterAsync<TProcess>(
        string?                       engine    = null,
        Action<ProcessConfiguration>? configure = null,
        CancellationToken             ct        = default
    )
        where TProcess : ProcessDefinition {
        var configuration = new ProcessConfiguration {
            Name           = typeof(TProcess).Name,
            Engine         = engine ?? SchemataConstants.FlowEngines.StateMachine,
            DefinitionType = typeof(TProcess),
        };

        configure?.Invoke(configuration);

        return RegisterAsync(configuration, ct);
    }

    public ValueTask RegisterAsync(
        string                        source,
        string?                       engine    = null,
        Action<ProcessConfiguration>? configure = null,
        CancellationToken             ct        = default
    ) {
        throw new NotSupportedException("Registering a process from a serialized source is not yet supported.");
    }

    public ValueTask UnregisterAsync(string processName, CancellationToken ct = default) {
        _registrations.Remove(processName);
        return default;
    }

    public IReadOnlyCollection<string> GetRegisteredProcesses() { return _registrations.Keys; }

    public bool IsRegistered(string name) { return _registrations.ContainsKey(name); }

    public ProcessRegistration? GetRegistration(string name) {
        _registrations.TryGetValue(name, out var registration);
        return registration;
    }

    public ValueTask RegisterAsync(ProcessConfiguration configuration, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.Name)) {
            throw new InvalidArgumentException(message: "Process name is required.");
        }

        if (_registrations.ContainsKey(configuration.Name)) {
            throw new AlreadyExistsException(message: $"Process '{configuration.Name}' is already registered.");
        }

        var definition = LoadDefinition(configuration);

        if (configuration.Engine.Equals(
                SchemataConstants.FlowEngines.StateMachine,
                StringComparison.OrdinalIgnoreCase
            )) {
            StateMachineValidator.Validate(definition);
        }

        var runtime = _services.GetKeyedService<IFlowRuntime>(configuration.Engine);
        if (runtime is null) {
            throw new NotSupportedException(
                $"Flow engine '{configuration.Engine}' is not registered. "
              + $"Process '{configuration.Name}' cannot be loaded."
            );
        }

        _registrations[configuration.Name] = new() {
            Name          = configuration.Name,
            Engine        = configuration.Engine,
            Definition    = definition,
            Configuration = configuration,
        };

        return default;
    }

    #endregion

    private static ProcessDefinition LoadDefinition(ProcessConfiguration configuration) {
        if (configuration.DefinitionType is not null && configuration.DefinitionType != typeof(ProcessDefinition)) {
            var instance = (ProcessDefinition?)Activator.CreateInstance(configuration.DefinitionType);
            if (instance is not null) {
                instance.Name = configuration.Name;
                return instance;
            }
        }

        return new() { Name = configuration.Name };
    }
}
