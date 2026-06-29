using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>In-memory <see cref="IProcessRegistry"/> backed by a thread-safe dictionary.</summary>
public sealed class ProcessRegistry : IProcessRegistry
{
    private readonly ConcurrentDictionary<string, ProcessRegistration> _registrations
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceProvider _services;

    /// <summary>Creates a new <see cref="ProcessRegistry"/> resolving dependencies from <paramref name="services"/>.</summary>
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

    public ValueTask UnregisterAsync(string processName, CancellationToken ct = default) {
        _registrations.TryRemove(processName, out var _);
        return default;
    }

    public IReadOnlyCollection<string> GetRegisteredProcesses() {
        return _registrations.Keys.ToList();
    }

    public bool IsRegistered(string name) { return _registrations.ContainsKey(name); }

    public ProcessRegistration? GetRegistration(string name) {
        _registrations.TryGetValue(name, out var registration);
        return registration;
    }

    public ValueTask RegisterAsync(ProcessConfiguration configuration, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.Name)) {
            throw new InvalidArgumentException(SchemataResources.PROCESS_NAME_REQUIRED);
        }

        var definition = LoadDefinition(configuration);

        foreach (var validator in _services.GetServices<IFlowEngineValidator>()) {
            if (string.Equals(validator.EngineName, configuration.Engine, StringComparison.OrdinalIgnoreCase)) {
                validator.Validate(definition);
            }
        }

        var registration = new ProcessRegistration {
            Name          = configuration.Name,
            Engine        = configuration.Engine,
            Definition    = definition,
            Configuration = configuration,
        };

        if (!_registrations.TryAdd(configuration.Name, registration)) {
            throw new AlreadyExistsException(
                SchemataResources.PROCESS_ALREADY_REGISTERED,
                new Dictionary<string, string> { ["name"] = configuration.Name });
        }

        return default;
    }

    #endregion

    private ProcessDefinition LoadDefinition(ProcessConfiguration configuration) {
        if (configuration.DefinitionType is null || configuration.DefinitionType == typeof(ProcessDefinition)) {
            return new() { Name = configuration.Name };
        }

        var instance = (ProcessDefinition)ActivatorUtilities.CreateInstance(_services, configuration.DefinitionType);
        instance.Name = configuration.Name;
        return instance;
    }
}
