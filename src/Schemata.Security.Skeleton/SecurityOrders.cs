using Schemata.Abstractions;

namespace Schemata.Security.Skeleton;

/// <summary>
    ///     Order anchors for the shared request wrap pipeline. Each stage sits 10,000,000 above the
    ///     previous one so per-feature advisors can slot between stages; the dispatcher composes the
    ///     chain in ascending <see cref="Schemata.Abstractions.Advisors.IAdvisor.Order" />.
/// </summary>
public static class SecurityOrders
{
    /// <summary>Authentication runs first on the wrap chain.</summary>
    public const int Authentication = SchemataConstants.Orders.Base;

    /// <summary>Coarse-grained authorization runs after authentication.</summary>
    public const int Authorization = Authentication + 10_000_000;

    /// <summary>Resource sanitize clears server-managed request fields after authorization.</summary>
    public const int Sanitize = Authorization + 10_000_000;

    /// <summary>Request validation reads the sanitized payload.</summary>
    public const int Validation = Sanitize + 10_000_000;

    /// <summary>Idempotency reserves or replays after validation accepts the request.</summary>
    public const int Idempotency = Validation + 10_000_000;

    /// <summary>Response aspects run on the handler's response last.</summary>
    public const int ResponseFamily = Idempotency + 10_000_000;
}
