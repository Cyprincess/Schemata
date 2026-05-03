namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses soft-delete interception on add and remove operations, per
///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso> and
///     <seealso href="https://google.aip.dev/214">AIP-214: Resource expiration</seealso>.
/// </summary>
public sealed class SoftDeleteSuppressed;
