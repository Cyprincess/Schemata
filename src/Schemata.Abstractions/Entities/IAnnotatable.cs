using System.Collections.Generic;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Declares the client-managed annotations map, corresponding to
///     <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>
///     <c>annotations</c>. Clients store small amounts of arbitrary data under
///     string keys; the framework reads the map only to persist and serve it.
/// </summary>
public interface IAnnotatable
{
    /// <summary>
    ///     The client-managed annotations. Keys and values are opaque to the framework.
    /// </summary>
    Dictionary<string, string?> Annotations { get; set; }
}
