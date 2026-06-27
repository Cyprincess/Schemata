using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a property as a cross-resource reference (mode B) carrying a full
///     <seealso href="https://google.aip.dev/122">AIP-122</seealso> canonical name
///     of an independent resource. The framework wires foreign-key relationships
///     against <see cref="ICanonicalName.CanonicalName" /> when <see cref="Target" />
///     is provided, and validates resolvability via
///     <see cref="IResourceTypeResolver" /> at write time.
/// </summary>
/// <remarks>
///     This is distinct from identity-composing parents (mode A), which the framework
///     identifies structurally via <c>[CanonicalName]</c> templates and a
///     <c>ResourceNameDescriptor</c>. Identity parents store the bare leaf id of the
///     parent segment; cross-resource references store the complete canonical name.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ResourceReferenceAttribute : Attribute
{
    /// <summary>
    ///     Initializes a polymorphic reference. The field accepts canonical names of any
    ///     registered resource; the ORM bridge emits no foreign-key configuration, and
    ///     write-time validation only requires <see cref="IResourceTypeResolver.Resolve(string)" />
    ///     to return a non-<see langword="null" /> type.
    /// </summary>
    public ResourceReferenceAttribute() {
        Target = null;
    }

    /// <summary>
    ///     Initializes a typed reference to a known entity type. The ORM bridge emits a
    ///     foreign-key to <see cref="ICanonicalName.CanonicalName" /> on
    ///     <paramref name="target" /> (an alternate key on the principal side), and
    ///     write-time validation requires the resolved type to equal <paramref name="target" />.
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
