using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a property carrying a full canonical resource reference. Repository advisors
///     validate type resolvability through <see cref="IResourceTypeResolver" /> and optionally
///     validate target existence. Database foreign keys, unique indexes and cascades remain
///     application mapping responsibilities.
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
    ///     write-time validation only requires <see cref="IResourceTypeResolver.Resolve(string)" />
    ///     to return a non-<see langword="null" /> type.
    /// </summary>
    public ResourceReferenceAttribute() {
        Target = null;
    }

    /// <summary>
    ///     Initializes a typed logical reference. Write-time validation requires the resolved
    ///     resource type to equal <paramref name="target" />.
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
    ///     owner-query suppression, so cross-owner references resolve. A missing row
    ///     surfaces as <c>NOT_FOUND</c> for the target type.
    /// </summary>
    /// <remarks>
    ///     Repository mutation advisors perform existence validation independently of ownership
    ///     filtering when this property opts in. The referenced entity must implement
    ///     <see cref="ICanonicalName" /> and have its repository registered. The check does not
    ///     prevent a concurrent target deletion after validation.
    /// </remarks>
    public bool ValidateExistence { get; set; }
}
