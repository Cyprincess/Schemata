using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>In-memory <see cref="IProcessRegistry"/> backed by a thread-safe dictionary.</summary>
public sealed class ProcessRegistry : IProcessRegistry
{
    private static readonly MethodInfo CompileMethod =
        typeof(ProcessRegistry).GetMethod(nameof(CompilePredicate), BindingFlags.NonPublic | BindingFlags.Static)!;

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

        CompileConditions(definition, configuration);

        var registration = new ProcessRegistration {
            Name               = configuration.Name,
            Engine             = configuration.Engine,
            Definition         = definition,
            Configuration      = configuration,
            SourceTypes        = BuildSourceDescriptors(definition),
            MessagePayloadTypes = CollectPayloadTypes(definition.Messages),
            SignalPayloadTypes  = CollectPayloadTypes(definition.Signals),
        };

        if (!_registrations.TryAdd(configuration.Name, registration)) {
            throw new AlreadyExistsException(
                SchemataResources.PROCESS_ALREADY_REGISTERED,
                new Dictionary<string, string?> { ["name"] = configuration.Name });
        }

        return default;
    }

    #endregion

    private void CompileConditions(ProcessDefinition definition, ProcessConfiguration configuration) {
        IExpressionCompiler? compiler = null;
        foreach (var flow in definition.AllFlows) {
            if (flow.Condition is not IStringConditionExpression condition || condition.Compiled) {
                continue;
            }

            compiler ??= ResolveCompiler(configuration);

            try {
                var predicate = (Delegate)CompileMethod.MakeGenericMethod(condition.SourceType)
                                                       .Invoke(null, [compiler, condition.Expression])!;
                condition.Bind(predicate);
            } catch (TargetInvocationException ex)
                when (ex.InnerException is ExpressionException or ArgumentException or InvalidOperationException) {
                throw new InvalidArgumentException(
                    SchemataResources.FLOW_EXPRESSION_INVALID,
                    new Dictionary<string, string?> { ["expression"] = condition.Expression });
            }
        }
    }

    private IExpressionCompiler ResolveCompiler(ProcessConfiguration configuration) {
        if (string.IsNullOrWhiteSpace(configuration.Language)) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_EXPRESSION_LANGUAGE_REQUIRED,
                new Dictionary<string, string?> { ["name"] = configuration.Name });
        }

        var compiler = _services.GetKeyedService<IExpressionCompiler>(configuration.Language);
        if (compiler is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_EXPRESSION_LANGUAGE_NOT_REGISTERED,
                new Dictionary<string, string?> { ["language"] = configuration.Language });
        }

        return compiler;
    }

    private static Func<TSource, bool> CompilePredicate<TSource>(IExpressionCompiler compiler, string expression)
        where TSource : class {
        return compiler.Compile<TSource, bool>(compiler.Parse(expression)).Compile();
    }

    private ProcessDefinition LoadDefinition(ProcessConfiguration configuration) {
        if (configuration.DefinitionType is null || configuration.DefinitionType == typeof(ProcessDefinition)) {
            return new() { Name = configuration.Name };
        }

        var created = ActivatorUtilities.CreateInstance(_services, configuration.DefinitionType);
        if (created is not ProcessDefinition instance) {
            throw new InvalidArgumentException(
                message: $"Process definition type '{configuration.DefinitionType.FullName}' must derive from ProcessDefinition.");
        }

        instance.Name = configuration.Name;
        return instance;
    }

    private IReadOnlyDictionary<string, FlowSourceDescriptor> BuildSourceDescriptors(ProcessDefinition definition) {
        var descriptors = new Dictionary<string, FlowSourceDescriptor>(StringComparer.Ordinal);
        foreach (var flow in definition.AllFlows) {
            if (flow.Condition is not ISourceCondition condition || string.IsNullOrEmpty(condition.Name)) {
                continue;
            }

            descriptors[condition.Name] = new() {
                BindingName = condition.Name,
                SourceType  = condition.SourceType,
                Projection  = FlowSourceProjection.Auto,
            };
        }

        var names   = new HashSet<string>(StringComparer.Ordinal);
        var members = new Dictionary<Type, HashSet<PropertyInfo>>();
        var logger  = _services.GetService<ILogger<ProcessRegistry>>();
        foreach (var declaration in definition.SourceDeclarations) {
            if (!names.Add(declaration.BindingName)) {
                throw new InvalidArgumentException(SchemataResources.PROCESS_SOURCE_BINDING_DUPLICATE);
            }

            if (descriptors.TryGetValue(declaration.BindingName, out var current)
             && current.SourceType != declaration.SourceType) {
                throw new InvalidArgumentException(SchemataResources.PROCESS_SOURCE_BINDING_DUPLICATE);
            }

            var stateMember     = ResolveSourceMember(declaration.SourceType, declaration.StateMember);
            var lifecycleMember = ResolveSourceMember(declaration.SourceType, declaration.LifecycleMember);
            ValidateMemberConflicts(members, declaration.SourceType, stateMember, lifecycleMember);

            descriptors[declaration.BindingName] = BuildSourceDescriptor(declaration, stateMember, lifecycleMember, logger);
        }

        return descriptors;
    }

    private static IReadOnlyDictionary<string, Type> CollectPayloadTypes<TEvent>(IEnumerable<TEvent> events)
        where TEvent : IEventDefinition {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var evt in events) {
            Type? payload = evt switch {
                Message { PayloadType: { } messagePayload } => messagePayload,
                Signal { PayloadType: { } signalPayload }   => signalPayload,
                _                                           => null,
            };

            if (payload is not null) {
                map[evt.Name] = payload;
            }
        }

        return map;
    }

    private static FlowSourceDescriptor BuildSourceDescriptor(
        FlowSourceDeclaration     declaration,
        PropertyInfo?              stateMember,
        PropertyInfo?              lifecycleMember,
        ILogger<ProcessRegistry>? logger
    ) {
        var projection = declaration.Projection ?? FlowSourceProjection.Auto;
        Func<object, string?>? getState = null;
        Action<object, string?>? setState = null;
        if (stateMember is not null) {
            getState = source => (string?)stateMember.GetValue(source);
            setState = (source, value) => stateMember.SetValue(source, value);
        } else if (typeof(IStateful).IsAssignableFrom(declaration.SourceType)) {
            getState = source => ((IStateful)source).State;
            setState = (source, value) => ((IStateful)source).State = value;
        } else if (projection != FlowSourceProjection.None) {
            logger?.LogWarning(
                "Source binding '{BindingName}' has no state member and will not receive state projection.",
                declaration.BindingName);
        }

        Func<object, string?>? getLifecycle = null;
        Action<object, string?>? setLifecycle = null;
        if (lifecycleMember is not null) {
            getLifecycle = source => (string?)lifecycleMember.GetValue(source);
            setLifecycle = (source, value) => lifecycleMember.SetValue(source, value);
        }

        return new() {
            BindingName  = declaration.BindingName,
            SourceType   = declaration.SourceType,
            Projection   = projection,
            GetState     = getState,
            SetState     = setState,
            GetLifecycle = getLifecycle,
            SetLifecycle = setLifecycle,
        };
    }

    private static PropertyInfo? ResolveSourceMember(Type sourceType, LambdaExpression? expression) {
        if (expression is null) {
            return null;
        }

        if (expression.Body is not MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression parameter }
         || parameter != expression.Parameters[0]
         || property.DeclaringType?.IsAssignableFrom(sourceType) != true
         || property.GetMethod is null
         || property.SetMethod is null
         || property.GetMethod.IsStatic
         || property.SetMethod.IsStatic) {
            throw new InvalidArgumentException(SchemataResources.PROCESS_SOURCE_MEMBER_INVALID);
        }

        return property;
    }

    private static void ValidateMemberConflicts(
        Dictionary<Type, HashSet<PropertyInfo>> members,
        Type                                    sourceType,
        PropertyInfo?                           stateMember,
        PropertyInfo?                           lifecycleMember
    ) {
        if (stateMember is null && lifecycleMember is null) {
            return;
        }

        if (!members.TryGetValue(sourceType, out var selected)) {
            selected = [];
            members.Add(sourceType, selected);
        }

        if (stateMember is not null && !selected.Add(stateMember)) {
            throw new InvalidArgumentException(SchemataResources.PROCESS_SOURCE_MEMBER_CONFLICT);
        }

        if (lifecycleMember is not null && !selected.Add(lifecycleMember)) {
            throw new InvalidArgumentException(SchemataResources.PROCESS_SOURCE_MEMBER_CONFLICT);
        }
    }
}
