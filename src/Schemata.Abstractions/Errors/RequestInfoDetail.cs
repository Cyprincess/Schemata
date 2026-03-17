using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

[Polymorphic(typeof(IErrorDetail))]
public class RequestInfoDetail : IErrorDetail
{
    public virtual string? RequestId { get; set; }

    public virtual string? ServingData { get; set; }

    #region IErrorDetail Members

    public string Type => "type.googleapis.com/google.rpc.RequestInfo";

    #endregion
}
