using System;

namespace Schemata.Abstractions.Json;

/// <summary>
///     Registers a class as a derived type of <see cref="BaseType" /> for
///     polymorphic JSON serialization. The serializer emits a discriminator
///     (defaults to <c>"@type"</c>) so the deserializer can select the correct
///     concrete type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PolymorphicAttribute : Attribute
{
    /// <summary>
    ///     Registers the annotated class as a derived type of <paramref name="type" />
    ///     for polymorphic serialization.
    /// </summary>
    /// <param name="type">The base type this class inherits from.</param>
    public PolymorphicAttribute(Type type) { BaseType = type; }

    /// <summary>
    ///     The base type under which this derived type is registered.
    /// </summary>
    public Type BaseType { get; }

    /// <summary>
    ///     The discriminator value emitted for this derived type. When
    ///     <see langword="null" />, the type's CLR name is used.
    /// </summary>
    public string? Name { get; set; }
}
