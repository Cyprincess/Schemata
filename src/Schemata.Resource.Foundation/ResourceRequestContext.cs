using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation;

/// <summary>
/// Carries the operation type and request payload for access-control evaluation.
/// </summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public class ResourceRequestContext<TRequest>
{
    /// <summary>
    /// Gets or sets the resource operation being performed.
    /// </summary>
    public Operations Operation { get; set; }

    /// <summary>
    /// Gets or sets the request payload, if available.
    /// </summary>
    public TRequest? Request { get; set; }
}
