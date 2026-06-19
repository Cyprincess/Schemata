using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail listing one or more <see cref="QuotaViolation" /> entries describing
///     exceeded quota limits, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.QuotaFailure")]
public class QuotaFailureDetail : IErrorDetail
{
    /// <summary>
    ///     Individual quota violations that collectively caused the error.
    /// </summary>
    public virtual List<QuotaViolation>? Violations { get; set; }
}
