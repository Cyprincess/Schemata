using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail describing field-level validation failures in a bad request.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class BadRequestDetail : IErrorDetail
{
    /// <summary>
    ///     Gets or sets the list of field violations that caused the bad request.
    /// </summary>
    public virtual List<ErrorFieldViolation>? FieldViolations { get; set; }

    #region IErrorDetail Members

    /// <inheritdoc />
    public string Type => "type.googleapis.com/google.rpc.BadRequest";

    #endregion
}
