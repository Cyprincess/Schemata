using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail carrying stack traces or other debugging information per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>. Hosts must
///     guard the population of this detail to non-production environments so internal
///     implementation details do not leak to API consumers.
/// </summary>
/// <remarks>
///     An extension detail payload: the framework defines and serializes the shape, but the
///     application layer decides when to attach it to an error. The framework core never
///     populates it on its own.
/// </remarks>
[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.DebugInfo")]
public class DebugInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Stack-frame strings ordered from innermost to outermost call.
    /// </summary>
    public virtual List<string>? StackEntries { get; set; }

    /// <summary>
    ///     Additional debugging information from the server runtime.
    /// </summary>
    public virtual string? Detail { get; set; }
}
