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
///         Persistence stays untouched: structural parent segments on the entity
///         (mode A, bare leaf id) are not affected by this trait. On the response
///         side, the framework derives <c>Parent</c> from the entity's canonical name
///         and writes it onto the DTO. On the request side, the framework parses the
///         supplied <c>Parent</c> back into the entity's mode A field(s) so a request
///         body can carry <c>tenants/t1</c> instead of relying on HTTP route values
///         only.
///     </para>
///     <para>
///         Intended for DTO types only. Do not implement <see cref="IChild" /> on a
///         persisted entity: the entity already stores its structural parent as a bare
///         leaf id (e.g. <c>Tenant = "t1"</c>), and an additional <c>Parent</c>
///         property would collide with mapper-driven copy from entity to DTO.
///     </para>
///     <para>
///         Implementing <see cref="IChild" /> on a DTO whose target entity has no
///         parent segment in its <c>[CanonicalName]</c> template results in a
///         <see langword="null" /> <see cref="Parent" /> on responses; no harm, just
///         no value.
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
