using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a property carrying a full canonical resource reference. Repository advisors
///     validate the referenced resource type and optionally validate target existence.
///     Typed references validate against the declared target type: without an
///     <see cref="IResourceTypeResolver" /> the value must match the target's own
///     <see cref="CanonicalNameAttribute" /> pattern; with a resolver the resolved type
///     must be assignable to the target. Polymorphic references resolve the target type from the
///     canonical name and therefore require a resolver. Database foreign keys, unique indexes and
///     cascades remain application mapping responsibilities.
/// </summary>
/// <remarks>
///     This is distinct from identity-composing parents (mode A), which the framework
///     identifies structurally via <c>[CanonicalName]</c> templates and a
///     <c>ResourceNameDescriptor</c>. Identity parents store the bare leaf id of the
///     parent segment; cross-resource references store the complete canonical name.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ResourceReferenceAttribute : Attribute
{
    /// <summary>
    ///     Initializes a polymorphic reference. The field accepts canonical names of any
    ///     registered resource; the ORM bridge emits no foreign-key configuration, and
    ///     write-time validation requires <see cref="IResourceTypeResolver.Resolve(string)" />
    ///     to return a non-<see langword="null" /> type. A repository-only host that persists
    ///     polymorphic references must register an <see cref="IResourceTypeResolver" />; a missing
    ///     registration is a configuration error, not a silent skip.
    /// </summary>
    public ResourceReferenceAttribute() {
        Target = null;
    }

    /// <summary>
    ///     Initializes a typed logical reference. Write-time validation requires the referenced
    ///     resource to be <paramref name="target" /> or a type assignable to it: with an
    ///     <see cref="IResourceTypeResolver" /> the canonical name must resolve to such a type, so a
    ///     host-defined subtype satisfies a reference declared against its built-in base; without a
    ///     resolver the canonical name must match <paramref name="target" />'s own
    ///     <see cref="CanonicalNameAttribute" /> pattern. An unrelated type fails.
    /// </summary>
    /// <param name="target">The referenced entity type.</param>
    public ResourceReferenceAttribute(Type target) {
        Target = target;
    }

    /// <summary>
    ///     The referenced entity type, or <see langword="null" /> for polymorphic references.
    /// </summary>
    public Type? Target { get; }

    /// <summary>
    ///     When <see langword="true" />, write-time validation also verifies the referenced
    ///     row exists, in addition to type resolvability. The existence query runs against
    ///     the target repository by <see cref="ICanonicalName.CanonicalName" /> with
    ///     <c>QueryOptions.IgnoreOwner</c>, so cross-owner references resolve. A typed reference
    ///     queries the declared target's repository directly; a polymorphic reference needs an
    ///     <see cref="IResourceTypeResolver" /> to select that repository. A missing row surfaces as
    ///     <c>NOT_FOUND</c> for the target type.
    /// </summary>
    /// <remarks>
    ///     Repository mutation advisors perform existence validation independently of ownership
    ///     filtering when this property opts in. The referenced entity must implement
    ///     <see cref="ICanonicalName" /> and have its repository registered. The check does not
    ///     prevent a concurrent target deletion after validation.
    /// </remarks>
    public bool ValidateExistence { get; set; }
}
