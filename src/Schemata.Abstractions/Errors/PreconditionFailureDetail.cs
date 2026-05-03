using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail describing one or more <see cref="PreconditionViolation" /> entries that
///     prevented the operation from proceeding, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class PreconditionFailureDetail : IErrorDetail
{
    /// <summary>
    ///     The set of preconditions that were not met.
    /// </summary>
    public virtual List<PreconditionViolation>? Violations { get; set; }

    #region IErrorDetail Members

    /// <summary>
    ///     Returns <c>"type.googleapis.com/google.rpc.PreconditionFailure"</c>.
    /// </summary>
    public string Type => "type.googleapis.com/google.rpc.PreconditionFailure";

    #endregion
}
