namespace Schemata.Security.Skeleton;

public class AccessContext<TRequest>
{
    /// <summary>CRUD or custom operation name being authorized, e.g. "Create" or "List".</summary>
    public string? Operation { get; set; }

    /// <summary>Incoming request payload, if any, for content-based authorization decisions.</summary>
    public TRequest? Request { get; set; }
}
