namespace Schemata.Entity.Repository;

/// <summary>Separates row existence from a projection whose value may be null.</summary>
public readonly record struct OriginalResult<T>(bool Found, T Value);
