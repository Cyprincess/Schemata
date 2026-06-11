namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Context marker carrying a validated, non-wildcard <c>read_mask</c> so the
///     response advisors can trim the outgoing DTOs
///     per <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso>.
/// </summary>
/// <param name="Mask">The comma-separated field paths requested by the client.</param>
public sealed record ReadMaskRequested(string Mask);
