namespace Schemata.Entity.Repository;

/// <summary>
///     Fixed binding namespace captured by repositories and units of work at construction.
///     Services bound to one tenant (or one resource partition) keep that namespace for their
///     whole lifetime; the namespace joins the source-key hash, so same physical source still
///     refuses cross-binding joins.
/// </summary>
/// <param name="Namespace">
///     The binding namespace; empty for the global default, which reads no ambient identity.
/// </param>
public sealed record RepositoryBinding(string Namespace = "")
{
    /// <summary>Gets the global default binding with an empty namespace.</summary>
    public static RepositoryBinding Default { get; } = new();
}
