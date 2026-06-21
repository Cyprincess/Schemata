using System;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     The relational operators a source driver can lower into its native query. The splitter pushes
///     a single-source subtree only as far as the driver's capabilities reach; the rest runs locally.
/// </summary>
[Flags]
public enum DriverCapabilities
{
    None    = 0,
    Filter  = 1 << 0,
    Compute = 1 << 1,
    Project = 1 << 2,
    Order   = 1 << 3,
    Group   = 1 << 4,
    Limit   = 1 << 5,
    Join    = 1 << 6,
    Nested  = 1 << 7,
    All     = Filter | Compute | Project | Order | Group | Limit | Join | Nested,
}
