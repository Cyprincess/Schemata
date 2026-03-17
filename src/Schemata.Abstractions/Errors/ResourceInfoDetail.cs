using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

[Polymorphic(typeof(IErrorDetail))]
public class ResourceInfoDetail : IErrorDetail
{
    public virtual string? ResourceType { get; set; }

    public virtual string? ResourceName { get; set; }

    public virtual string? Owner { get; set; }

    public virtual string? Description { get; set; }

    #region IErrorDetail Members

    public string Type => "type.googleapis.com/google.rpc.ResourceInfo";

    #endregion
}
