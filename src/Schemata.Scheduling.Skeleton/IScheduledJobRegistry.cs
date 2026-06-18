using System;
using System.Collections.Generic;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Maps stable scheduler job keys to concrete <see cref="IScheduledJob" /> types.</summary>
public interface IScheduledJobRegistry
{
    /// <summary>Registers a concrete job type under a stable key.</summary>
    void Register(Type jobType, string key);

    /// <summary>Registers a concrete job type using the supplied key or its full type name.</summary>
    void Register<T>(string? key = null)
        where T : class, IScheduledJob;

    /// <summary>Resolves a stable job key to the registered concrete job type.</summary>
    Type? Resolve(string key);

    /// <summary>Resolves a concrete job type to its stable job key.</summary>
    string? ResolveKey(Type jobType);

    /// <summary>Registers all concrete scheduled job types in the supplied sequence.</summary>
    void RegisterAll(IEnumerable<Type> jobTypes);
}
