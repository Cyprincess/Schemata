using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail listing one or more <see cref="QuotaViolation" /> entries describing
///     quota limits that were exceeded, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class QuotaFailureDetail : IErrorDetail
{
    /// <summary>
    ///     Individual quota violations that collectively caused the error.
    /// </summary>
    public virtual List<QuotaViolation>? Violations { get; set; }

    #region IErrorDetail Members

    /// <summary>
    ///     Returns <c>"type.googleapis.com/google.rpc.QuotaFailure"</c>.
    /// </summary>
    public string Type => "type.googleapis.com/google.rpc.QuotaFailure";

    #endregion
}
