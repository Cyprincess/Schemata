namespace Schemata.Abstractions;

/// <summary>
///     Sentinel type used as a type argument when an advisor interface requires a request
///     type but the operation has no request body.
/// </summary>
public sealed class Unit
{
    /// <summary>Singleton instance.</summary>
    public static readonly Unit Value = new();

    private Unit() { }
}
