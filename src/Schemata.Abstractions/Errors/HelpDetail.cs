using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail listing one or more documentation links the caller can follow for
///     supplemental troubleshooting information, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
/// <remarks>
///     Extension detail payload whose shape is defined by the framework and attached by
///     the application layer.
/// </remarks>
[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.Help")]
public class HelpDetail : IErrorDetail
{
    /// <summary>
    ///     Help links the caller may follow for additional context about this error.
    /// </summary>
    public virtual List<ErrorHelpLink>? Links { get; set; }
}
