using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail describing one or more quota violations.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class QuotaFailureDetail : IErrorDetail
{
    /// <summary>
    ///     Gets or sets the list of quota violations.
    /// </summary>
    public virtual List<QuotaViolation>? Violations { get; set; }

    #region IErrorDetail Members

    /// <inheritdoc />
    public string Type => "type.googleapis.com/google.rpc.QuotaFailure";

    #endregion
}
