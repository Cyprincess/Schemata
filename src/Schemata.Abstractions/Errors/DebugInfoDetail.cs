using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail carrying stack traces or other debugging information per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>. Hosts should
///     populate this detail only in non-production environments to keep internal
///     implementation details away from API consumers.
/// </summary>
/// <remarks>
///     Extension detail payload whose shape is defined by the framework and attached by
///     the application layer.
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
