namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses automatic <see cref="Schemata.Abstractions.Entities.ITimestamp.CreateTime" />
///     and <see cref="Schemata.Abstractions.Entities.ITimestamp.UpdateTime" /> assignment, per
///     <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>.
/// </summary>
public sealed class TimestampSuppressed;
