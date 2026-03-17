using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

[Polymorphic(typeof(IErrorDetail))]
public class BadRequestDetail : IErrorDetail
{
    public virtual List<ErrorFieldViolation>? FieldViolations { get; set; }

    #region IErrorDetail Members

    public string Type => "type.googleapis.com/google.rpc.BadRequest";

    #endregion
}
