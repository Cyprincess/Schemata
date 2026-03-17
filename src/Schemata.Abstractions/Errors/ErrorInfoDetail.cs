using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

[Polymorphic(typeof(IErrorDetail))]
public class ErrorInfoDetail : IErrorDetail
{
    public virtual string? Reason { get; set; }

    public virtual string? Domain { get; set; }

    public virtual Dictionary<string, string>? Metadata { get; set; }

    #region IErrorDetail Members

    public string Type => "type.googleapis.com/google.rpc.ErrorInfo";

    #endregion
}
