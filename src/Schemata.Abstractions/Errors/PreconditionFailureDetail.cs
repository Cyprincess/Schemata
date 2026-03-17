using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

[Polymorphic(typeof(IErrorDetail))]
public class PreconditionFailureDetail : IErrorDetail
{
    public virtual List<PreconditionViolation>? Violations { get; set; }

    #region IErrorDetail Members

    public string Type => "type.googleapis.com/google.rpc.PreconditionFailure";

    #endregion
}
