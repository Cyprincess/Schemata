using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail providing information about the resource involved in the error.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class ResourceInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Gets or sets the type of resource (e.g., "Book").
    /// </summary>
    public virtual string? ResourceType { get; set; }

    /// <summary>
    ///     Gets or sets the name of the resource.
    /// </summary>
    public virtual string? ResourceName { get; set; }

    /// <summary>
    ///     Gets or sets the owner of the resource.
    /// </summary>
    public virtual string? Owner { get; set; }

    /// <summary>
    ///     Gets or sets a human-readable description.
    /// </summary>
    public virtual string? Description { get; set; }

    #region IErrorDetail Members

    /// <inheritdoc />
    public string Type => "type.googleapis.com/google.rpc.ResourceInfo";

    #endregion
}
