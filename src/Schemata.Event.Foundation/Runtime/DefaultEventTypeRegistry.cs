using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Runtime;

/// <summary>
///     Concurrent-dictionary backed <see cref="IEventTypeRegistry" />. Registrations come from
///     <see cref="Builders.EventBuilder.RegisterEvent{TEvent}" /> at startup; lookups happen on
///     every publish/consume path.
/// </summary>
public sealed class DefaultEventTypeRegistry : IEventTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type>       _byName  = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string>       _byType  = new();
    private readonly ConcurrentDictionary<Type, EventRouting> _routing = new();

    #region IEventTypeRegistry Members

    public void Register(Type type, string name) {
        if (type is null) {
            throw new ArgumentNullException(nameof(type));
        }

        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Event name must not be empty.", nameof(name));
        }

        if (_byType.TryGetValue(type, out var existingName) && existingName != name) {
            throw new InvalidOperationException($"Event type '{
                type
            }' is already registered as '{
                existingName
            }' and cannot be re-registered as '{
                name
            }'.");
        }

        if (_byName.TryGetValue(name, out var existingType) && existingType != type) {
            throw new InvalidOperationException($"Event name '{
                name
            }' is already registered to '{
                existingType
            }' and cannot be re-registered to '{
                type
            }'.");
        }

        _byType[type] = name;
        _byName[name] = type;
    }

    /// <summary>Sets the delivery mode for <paramref name="type" />.</summary>
    public void SetRouting(Type type, EventRouting routing) {
        if (type is null) {
            throw new ArgumentNullException(nameof(type));
        }

        _routing[type] = routing;
    }

    public string? GetName(Type type) { return _byType.GetValueOrDefault(type); }

    public Type? Resolve(string name) { return _byName.GetValueOrDefault(name); }

    public EventRouting GetRouting(Type type) {
        return _routing.GetValueOrDefault(type, EventRouting.Broadcast);
    }

    public string RequireName(Type type) {
        return _byType.TryGetValue(type, out var name)
            ? name
            : throw new InvalidOperationException($"Event type '{
                type
            }' is not registered. Call EventBuilder.RegisterEvent<{
                type.Name
            }>(\"name\") during startup.");
    }

    #endregion
}
