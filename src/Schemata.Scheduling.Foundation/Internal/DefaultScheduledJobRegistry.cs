using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed class DefaultScheduledJobRegistry : IScheduledJobRegistry
{
    private readonly ConcurrentDictionary<string, Type> _byKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _byType = new();

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
        return _byKey.TryGetValue(key, out var jobType) ? jobType : null;
    }

    public string? ResolveKey(Type jobType) {
        ArgumentNullException.ThrowIfNull(jobType);
        return _byType.TryGetValue(jobType, out var key) ? key : null;
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
