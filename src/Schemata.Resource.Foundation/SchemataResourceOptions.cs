using System;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Configuration for the resource system: global validation and freshness suppression,
///     authentication scheme, pagination and idempotency policy. The registered resources themselves
///     live in <see cref="IResourceRegistry" />, not here.
/// </summary>
public sealed class SchemataResourceOptions
{
    /// <summary>
    ///     Gets or sets whether create-request validation is globally suppressed.
    /// </summary>
    public bool SuppressCreateValidation { get; set; }

    /// <summary>
    ///     Gets or sets whether update-request validation is globally suppressed.
    /// </summary>
    public bool SuppressUpdateValidation { get; set; }

    /// <summary>
    ///     Gets or sets whether freshness (ETag) checks and generation are globally suppressed
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </summary>
    public bool SuppressFreshness { get; set; }

    /// <summary>
    ///     Gets or sets the default authentication scheme for resource endpoints.
    ///     When <see langword="null" />, no authorization policy is attached and the endpoints
    ///     are reachable without authenticating.
    ///     Overridable per resource via <see cref="ResourceAttribute.AuthenticationScheme" />.
    /// </summary>
    public string? AuthenticationScheme { get; set; }

    /// <summary>
    ///     Gets or sets the global <c>total_size</c> computation mode for list responses.
    ///     <see cref="TotalSizeMode.Default" /> behaves as <see cref="TotalSizeMode.Exact" />.
    ///     Overridable per resource via <see cref="ResourceAttribute.TotalSize" />.
    /// </summary>
    public TotalSizeMode TotalSize { get; set; }

    /// <summary>
    ///     Gets or sets how long a reserved (pending) or finalized idempotency record is retained,
    ///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
    ///     The pending reservation must outlive the operation so a replay arriving after a
    ///     commit-then-crash observes the reservation and waits for the finalized result.
    /// </summary>
    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     Gets or sets how long a replayed request waits for an in-flight first attempt to
    ///     finalize its result before reporting <c>ABORTED</c> so the client retries.
    /// </summary>
    public TimeSpan IdempotencyPendingWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets or sets the filter expression languages this resource module enables, in priority
    ///     order; the first is the default when a request omits an explicit language.
    /// </summary>
    public ExpressionLanguageProfile Expressions { get; set; } = new();
}
