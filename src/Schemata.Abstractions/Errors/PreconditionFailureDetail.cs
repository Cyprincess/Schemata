using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail describing one or more precondition violations that prevented the operation.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class PreconditionFailureDetail : IErrorDetail
{
    /// <summary>
    ///     Gets or sets the list of precondition violations.
    /// </summary>
    public virtual List<PreconditionViolation>? Violations { get; set; }

    #region IErrorDetail Members

    /// <inheritdoc />
    public string Type => "type.googleapis.com/google.rpc.PreconditionFailure";

    #endregion
}
