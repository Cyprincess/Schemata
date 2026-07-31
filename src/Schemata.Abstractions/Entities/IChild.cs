namespace Schemata.Abstractions.Entities;

/// <summary>
///     Marks a resource DTO (request, detail, or summary) as a child of another AIP
///     resource. <see cref="Parent" /> is a derived view of the resource's
///     <see cref="ICanonicalName.CanonicalName" /> minus its own collection and leaf
///     segments, materialized by the
///     <c>Schemata.Resource.Foundation.Advisors</c> pipeline.
/// </summary>
/// <remarks>
///     <para>
///         The trait only affects response/request projection; structural parent
///         segments on the entity (mode A, bare leaf id) are not affected. On the
///         response side, the framework derives <c>Parent</c> from the entity's
///         canonical name and writes it onto the DTO.
///     </para>
///     <para>
///         On the request side the URI decides the parent. Create overwrites
///         <c>Parent</c> from the route when the route carries every parent segment,
///         and falls back to the body otherwise; Update clears <c>Parent</c> before the
///         advisor chain, so a request body cannot move a resource to another parent.
///         The framework then parses the surviving <c>Parent</c> back into the entity's
///         mode A field(s).
///     </para>
///     <para>
///         Intended for DTO types only. Do not implement <see cref="IChild" /> on a
///         persisted entity: the entity already stores its structural parent as a bare
///         leaf id (e.g. <c>Tenant = "t1"</c>), and an additional <c>Parent</c>
///         property would collide with mapper-driven copy from entity to DTO.
///     </para>
///     <para>
///         Implementing <see cref="IChild" /> on a DTO whose target entity has no
///         parent segment in its <c>[CanonicalName]</c> template leaves
///         <see cref="Parent" /> as <see langword="null" /> on responses.
///     </para>
/// </remarks>
public interface IChild
{
    /// <summary>
    ///     Full <seealso href="https://google.aip.dev/122">AIP-122</seealso> canonical
    ///     name of the parent resource (e.g. <c>tenants/t1</c> for a host whose own
    ///     canonical name is <c>tenants/t1/hosts/h1</c>).
    /// </summary>
    string? Parent { get; set; }
}
