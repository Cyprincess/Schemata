using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Scheduling.Foundation.Runtime;

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
        Register(typeof(T), key ?? DeclaredKey(typeof(T)));
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

        var declared = DeclaredKey(jobType);
        Register(jobType, declared);
        return declared;
    }

    public void RegisterAll(IEnumerable<Type> jobTypes) {
        ArgumentNullException.ThrowIfNull(jobTypes);

        foreach (var jobType in jobTypes) {
            if (!typeof(IScheduledJob).IsAssignableFrom(jobType) || jobType is { IsAbstract: true } or { IsInterface: true }) {
                continue;
            }

            Register(jobType, DeclaredKey(jobType));
        }
    }

    /// <summary>
    ///     Reads the key from <see cref="ScheduledJobAttribute" />, else derives one by dropping
    ///     assembly qualification and generic arity and appending generic arguments as dotted short
    ///     names. A closed generic's assembly-qualified CLR name runs past 200 characters and carries
    ///     <c>`</c>, <c>[[</c>, <c>,</c>, <c>=</c> and spaces, which both a <c>jobs/{job}</c> segment
    ///     and the persisted key column reject.
    /// </summary>
    private static string DeclaredKey(Type jobType) {
        if (jobType.GetCustomAttribute<ScheduledJobAttribute>() is { Key: var declared } && !string.IsNullOrWhiteSpace(declared)) {
            return declared;
        }

        var builder = new StringBuilder(StripArity(jobType.FullName ?? jobType.Name));
        AppendArguments(builder, jobType);
        return builder.ToString();

        static void AppendArguments(StringBuilder builder, Type type) {
            if (!type.IsGenericType) {
                return;
            }

            foreach (var argument in type.GetGenericArguments()) {
                builder.Append('.').Append(StripArity(argument.Name));
                AppendArguments(builder, argument);
            }
        }

        static string StripArity(string name) {
            var arity = name.IndexOf('`');
            return arity < 0 ? name : name[..arity];
        }
    }
}
