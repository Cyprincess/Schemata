using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail identifying the resource that was the target or subject of a
///     failed operation, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class ResourceInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Kind of resource (e.g. <c>"Book"</c>, <c>"User"</c>).
    /// </summary>
    public virtual string? ResourceType { get; set; }

    /// <summary>
    ///     Unique, canonical name of the resource (often a fully-qualified resource name).
    /// </summary>
    public virtual string? ResourceName { get; set; }

    /// <summary>
    ///     Owning entity of the resource (e.g. project number, user identifier).
    /// </summary>
    public virtual string? Owner { get; set; }

    /// <summary>
    ///     Additional human-readable context about the resource.
    /// </summary>
    public virtual string? Description { get; set; }

    #region IErrorDetail Members

    /// <summary>
    ///     Returns <c>"type.googleapis.com/google.rpc.ResourceInfo"</c>.
    /// </summary>
    public string Type => "type.googleapis.com/google.rpc.ResourceInfo";

    #endregion
}
