using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

[Polymorphic(typeof(IErrorDetail))]
public class QuotaFailureDetail : IErrorDetail
{
    public virtual List<QuotaViolation>? Violations { get; set; }

    #region IErrorDetail Members

    public string Type => "type.googleapis.com/google.rpc.QuotaFailure";

    #endregion
}
