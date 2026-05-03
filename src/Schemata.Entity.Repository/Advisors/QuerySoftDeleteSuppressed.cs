namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses the soft-delete global query filter so that queries return
///     soft-deleted entities, per
///     <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso> and
///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
/// </summary>
public sealed class QuerySoftDeleteSuppressed;
