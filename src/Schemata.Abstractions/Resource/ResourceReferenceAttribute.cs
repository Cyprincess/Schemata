using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a property carrying a cross-resource association: the string holds the full
///     canonical name of the referenced resource. Canonical-name association is the framework
///     policy; the attribute is declarative metadata and the framework performs no write-time
///     validation of the value.
/// </summary>
/// <remarks>
///     <para>
///         A framework user that needs type or existence checks supplies them in its own business
///         logic or repository advisor; the framework does not create foreign keys, unique indexes,
///         cascades or referential-integrity guarantees from this marker.
///     </para>
///     <para>
///         This is distinct from identity-composing parents (mode A), which the framework
///         identifies structurally via <c>[CanonicalName]</c> templates and a
///         <c>ResourceNameDescriptor</c>. Identity parents store the bare leaf id of the
///         parent segment; cross-resource references store the complete canonical name.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ResourceReferenceAttribute : Attribute
{
    /// <summary>
    ///     Initializes a polymorphic reference: the field accepts canonical names of any
    ///     registered resource and no target type is declared.
    /// </summary>
    public ResourceReferenceAttribute() {
        Target = null;
    }

    /// <summary>
    ///     Initializes a typed logical reference to <paramref name="target" />. The declared type is
    ///     descriptive metadata for consumers of the marker; the framework does not validate the
    ///     value against it.
    /// </summary>
    /// <param name="target">The referenced entity type.</param>
    public ResourceReferenceAttribute(Type target) {
        Target = target;
    }

    /// <summary>
    ///     The referenced entity type, or <see langword="null" /> for polymorphic references.
    /// </summary>
    public Type? Target { get; }
}
