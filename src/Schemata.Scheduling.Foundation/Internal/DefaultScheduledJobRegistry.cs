using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Scheduling.Foundation.Internal;

/// <summary>Thread-safe registry that maps scheduled job keys to concrete job types.</summary>
public sealed class DefaultScheduledJobRegistry : IScheduledJobRegistry
{
    private readonly ConcurrentDictionary<string, Type>   _byKey  = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string>   _byType = new();
    private readonly IReadOnlyList<IScheduledJobKeyResolver> _resolvers;

    public DefaultScheduledJobRegistry(IEnumerable<IScheduledJobKeyResolver>? resolvers = null) {
        _resolvers = resolvers is null ? Array.Empty<IScheduledJobKeyResolver>() : [..resolvers];
    }

    public void Register(Type jobType, string key) {
        ArgumentNullException.ThrowIfNull(jobType);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!typeof(IScheduledJob).IsAssignableFrom(jobType) || jobType is { IsAbstract: true } or { IsInterface: true }) {
            return;
        }

        _byKey[key]      = jobType;
        _byType[jobType] = key;
    }

    public void Register<T>(string? key = null)
        where T : class, IScheduledJob {
        Register(typeof(T), key ?? typeof(T).FullName!);
    }

    public Type? Resolve(string key) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_byKey.TryGetValue(key, out var jobType)) {
            return jobType;
        }

        foreach (var resolver in _resolvers) {
            if (resolver.ResolveType(key) is { } resolved) {
                Register(resolved, key);
                return resolved;
            }
        }

        return null;
    }

    public string? ResolveKey(Type jobType) {
        ArgumentNullException.ThrowIfNull(jobType);
        if (_byType.TryGetValue(jobType, out var key)) {
            return key;
        }

        foreach (var resolver in _resolvers) {
            if (resolver.ResolveKey(jobType) is { } resolved) {
                Register(jobType, resolved);
                return resolved;
            }
        }

        return null;
    }

    public void RegisterAll(IEnumerable<Type> jobTypes) {
        ArgumentNullException.ThrowIfNull(jobTypes);

        foreach (var jobType in jobTypes) {
            if (!typeof(IScheduledJob).IsAssignableFrom(jobType) || jobType is { IsAbstract: true } or { IsInterface: true }) {
                continue;
            }

            var key = jobType.GetCustomAttribute<ScheduledJobAttribute>()?.Key ?? jobType.FullName;
            if (!string.IsNullOrWhiteSpace(key)) {
                Register(jobType, key);
            }
        }
    }
}
