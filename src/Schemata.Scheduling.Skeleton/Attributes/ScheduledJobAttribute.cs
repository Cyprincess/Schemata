using System;

namespace Schemata.Scheduling.Skeleton.Attributes;

/// <summary>Declares the stable scheduler key for an <see cref="IScheduledJob" /> type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScheduledJobAttribute(string key) : Attribute
{
    /// <summary>Stable key persisted in scheduler job rows.</summary>
    public string Key { get; } = key;
}
