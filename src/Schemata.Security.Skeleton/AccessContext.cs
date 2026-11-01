namespace Schemata.Security.Skeleton;

/// <summary>Provides authorization inputs for an operation.</summary>
/// <typeparam name="TRequest">Request payload type used by the authorized operation.</typeparam>
public class AccessContext<TRequest>
{
    /// <summary>CRUD or custom operation name being authorized, e.g. "Create" or "List".</summary>
    public string? Operation { get; set; }

    /// <summary>Incoming request payload, if any, for content-based authorization decisions.</summary>
    public TRequest? Request { get; set; }

    /// <summary>The authorization phase this evaluation runs in; defaults to <see cref="AccessStage.Target" />.</summary>
    public AccessStage Stage { get; set; } = AccessStage.Target;

    /// <summary>The requested target's name when the operation addresses an instance.</summary>
    public string? Name { get; set; }

    /// <summary>The applicable collection parent when the operation addresses a member of one.</summary>
    public string? Parent { get; set; }
}
