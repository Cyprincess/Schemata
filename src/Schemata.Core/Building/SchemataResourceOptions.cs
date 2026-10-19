using System;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;

namespace Schemata.Core.Building;

/// <summary>
///     Configuration for the resource system: authentication scheme, pagination, and idempotency
///     policy. The registered resources themselves live in <see cref="ResourceRegistry" />, not
///     here. Validation and freshness capabilities are installation choices made through
///     <see cref="SchemataResourceBuilder" /> at configuration time.
/// </summary>
public sealed class SchemataResourceOptions
{
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
    ///     Gets or sets the global default page size for List responses, per
    ///     <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso>. Requests that
    ///     omit <c>page_size</c> (or send zero) use this value. Must be positive and no greater
    ///     than <see cref="MaxPageSize" />. Overridable per resource via
    ///     <see cref="ResourceAttribute.DefaultPageSize" />.
    /// </summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>
    ///     Gets or sets the global maximum page size for List responses, per
    ///     <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso>. Requests
    ///     above this value coerce to it instead of failing. Must be positive and no smaller
    ///     than <see cref="DefaultPageSize" />. Overridable per resource via
    ///     <see cref="ResourceAttribute.MaxPageSize" />.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

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
