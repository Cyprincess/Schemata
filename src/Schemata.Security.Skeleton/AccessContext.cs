namespace Schemata.Security.Skeleton;

/// <summary>Provides authorization inputs for an operation.</summary>
/// <typeparam name="TRequest">Request payload type used by the authorized operation.</typeparam>
public class AccessContext<TRequest>
{
    /// <summary>CRUD or custom operation name being authorized, e.g. "Create" or "List".</summary>
    public string? Operation { get; set; }

    /// <summary>Incoming request payload, if any, for content-based authorization decisions.</summary>
    public TRequest? Request { get; set; }
}
