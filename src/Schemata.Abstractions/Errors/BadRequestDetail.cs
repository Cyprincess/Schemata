using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail listing one or more <see cref="ErrorFieldViolation" /> entries for a
///     rejected request whose fields failed validation, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class BadRequestDetail : IErrorDetail
{
    /// <summary>
    ///     Field-level violations that caused the request to be rejected.
    /// </summary>
    public virtual List<ErrorFieldViolation>? FieldViolations { get; set; }

    #region IErrorDetail Members

    /// <summary>
    ///     Returns <c>"type.googleapis.com/google.rpc.BadRequest"</c>.
    /// </summary>
    public string Type => "type.googleapis.com/google.rpc.BadRequest";

    #endregion
}
