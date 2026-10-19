namespace Schemata.Entity.Repository;

/// <summary>
///     The outcome of a single write operation. <see cref="Applied" /> means the provider staged or
///     executed the write; it carries no entity payload and does not imply a durable commit.
/// </summary>
public enum MutationResult
{
    /// <summary>The operation wrote nothing (an advisor blocked or handled it without staging).</summary>
    NoWrite,

    /// <summary>The operation staged or executed a write against the provider.</summary>
    Applied,
}
