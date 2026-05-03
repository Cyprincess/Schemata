using System;

namespace Schemata.Core.Features;

/// <summary>
///     Declares a typed feature dependency. The dependency is automatically registered
///     before the declaring feature during <see cref="SchemataOptionsExtensions.AddFeature{T}" />.
/// </summary>
/// <typeparam name="T">The <see cref="ISimpleFeature" /> type this feature depends on.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute<T> : Attribute
    where T : class, ISimpleFeature;
