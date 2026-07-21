using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Declares the properties that form an entity's primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PrimaryKeyAttribute(params string[] properties) : Attribute
{
    /// <summary>
    ///     Gets the primary-key properties in declaration order.
    /// </summary>
    public string[] Properties { get; } = properties;
}
