using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail describing one or more <see cref="PreconditionViolation" /> entries that
///     prevented the operation from proceeding, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.PreconditionFailure")]
public class PreconditionFailureDetail : IErrorDetail
{
    /// <summary>
    ///     The preconditions that blocked the operation.
    /// </summary>
    public virtual List<PreconditionViolation>? Violations { get; set; }
}
