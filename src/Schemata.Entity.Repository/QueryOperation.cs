namespace Schemata.Entity.Repository;

/// <summary>
///     The terminal operation a repository query applies after the pipelined queryable. Cache
///     keys distinguish these operations: the same queryable under different operations yields
///     different results and different failure modes. A cached <see cref="FirstOrDefault" />
///     result does not satisfy a <see cref="SingleOrDefault" /> query, whose duplicate-row
///     contract differs.
/// </summary>
public enum QueryOperation
{
    /// <summary>The first row of the result set, or the projected default when empty.</summary>
    FirstOrDefault,

    /// <summary>The single row of the result set, or the projected default when empty.</summary>
    SingleOrDefault,

    /// <summary>Whether the result set contains any row.</summary>
    Any,

    /// <summary>The 32-bit row count of the result set.</summary>
    Count,

    /// <summary>The 64-bit row count of the result set.</summary>
    LongCount
}
