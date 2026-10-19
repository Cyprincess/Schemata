namespace Schemata.Security.Skeleton;

/// <summary>
///     The authorization phase an access evaluation runs in: the operation target as a whole, a
///     loaded instance, or the disclosure decision for a target the load could not produce.
/// </summary>
public enum AccessStage
{
    /// <summary>The operation is evaluated without a loaded instance (create, collection).</summary>
    Target = 0,

    /// <summary>The operation is evaluated against a loaded entity instance.</summary>
    Instance = 1,

    /// <summary>
    ///     The load produced no instance; the provider decides whether the caller's policy
    ///     permits disclosing the absence (or a create-on-missing continuation) at all.
    /// </summary>
    Missing = 2,
}