using System;

namespace Schemata.Core.Features;

/// <summary>
///     Declares that a feature depends on another feature identified by type. The dependency is automatically registered.
/// </summary>
/// <typeparam name="T">The feature type that this feature depends on.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute<T> : Attribute
    where T : class, ISimpleFeature;
