using System;
using Schemata.Common;

namespace Schemata.Core.Features;

/// <summary>
///     Declares a string-based feature dependency resolved at registration time via
///     <see cref="AppDomainTypeCache" />. Set <see cref="Optional" /> to
///     <see langword="true" /> to suppress the error-level log when the dependency is
///     absent.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute : Attribute
{
    /// <param name="name">Assembly-qualified type name of the dependency.</param>
    public DependsOnAttribute(string name) { Name = name; }

    /// <summary>
    ///     Assembly-qualified type name of the dependent feature.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     When <see langword="true" />, a missing dependency logs at
    ///     <see cref="Microsoft.Extensions.Logging.LogLevel.Information" /> instead of
    ///     <see cref="Microsoft.Extensions.Logging.LogLevel.Error" />.
    /// </summary>
    public bool Optional { get; init; }
}
