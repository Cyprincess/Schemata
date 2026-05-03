namespace Schemata.Abstractions;

/// <summary>
///     Sentinel type used when an advisor interface requires a type argument
///     but the operation has no request body. Replaces the need for a separate
///     interface overload per operation.
/// </summary>
public sealed class Unit
{
    /// <summary>
    ///     Singleton instance; no state, so reuse is safe.
    /// </summary>
    public static readonly Unit Value = new();

    private Unit() { }
}
