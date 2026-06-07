namespace Schemata.Abstractions.Resource;

/// <summary>
///     Selects the binding scope of an AIP-136 custom Resource method:
///     a single resource instance, or the whole collection.
/// </summary>
public enum ResourceMethodScope
{
    /// <summary>
    ///     Bound to a single resource instance. HTTP path takes the form
    ///     <c>POST {collection}/{name}:{verb}</c>; the request carries the
    ///     resource <c>name</c>.
    /// </summary>
    Instance,

    /// <summary>
    ///     Bound to the collection as a whole. HTTP path takes the form
    ///     <c>POST {collection}:{verb}</c>; the request carries the collection
    ///     <c>parent</c> when applicable.
    /// </summary>
    Collection,
}
