namespace Schemata.Abstractions.Resource;

/// <summary>
///     HTTP method for an AIP-136 custom method.
///     <seealso href="https://google.aip.dev/136">AIP-136</seealso> permits only these two:
///     <see cref="Get" /> for read-only methods, <see cref="Post" /> for everything else.
/// </summary>
public enum ResourceHttpMethod
{
    /// <summary>
    ///     The method mutates state, has side effects, or is billed.
    /// </summary>
    Post,

    /// <summary>
    ///     The method is read-only; the request binds from the query string.
    /// </summary>
    Get,
}
