using System;

namespace Schemata.Abstractions.Json;

/// <summary>
///     Registers a class as a polymorphic derived type for JSON serialization under the specified base type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PolymorphicAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PolymorphicAttribute" /> class.
    /// </summary>
    /// <param name="type">The base type this class is a derived type of for polymorphic serialization.</param>
    public PolymorphicAttribute(Type type) { BaseType = type; }

    /// <summary>
    ///     Gets the base type used for polymorphic serialization.
    /// </summary>
    public Type BaseType { get; }

    /// <summary>
    ///     Gets or sets the type discriminator name used during serialization.
    /// </summary>
    public string? Name { get; set; }
}
