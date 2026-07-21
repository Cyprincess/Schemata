using System;
using Schemata.Common;

namespace Schemata.Core.Features;

/// <summary>
///     Declares a feature dependency resolved at registration time. String names resolve
///     through <see cref="AppDomainTypeCache" /> and type references are used directly.
///     Set <see cref="Optional" /> to <see langword="true" /> to suppress the error-level
///     log when the dependency is absent.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute : Attribute
{
    /// <summary>
    ///     Initializes a dependency declaration for a feature type name.
    /// </summary>
    /// <param name="name">Assembly-qualified type name of the dependency.</param>
    public DependsOnAttribute(string name) { Name = name; }

    /// <summary>
    ///     Initializes a dependency declaration for a feature type.
    /// </summary>
    /// <param name="type">The dependency feature type.</param>
    public DependsOnAttribute(Type type) {
        Type = type;
        Name = type.FullName ?? type.Name;
    }

    /// <summary>
    ///     Assembly-qualified type name of the dependent feature.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Dependency feature type when declared with the type-based constructor.
    /// </summary>
    public Type? Type { get; }

    /// <summary>
    ///     When <see langword="true" />, a missing dependency logs at
    ///     <see cref="Microsoft.Extensions.Logging.LogLevel.Information" /> instead of
    ///     <see cref="Microsoft.Extensions.Logging.LogLevel.Error" />.
    /// </summary>
    public bool Optional { get; init; }
}
