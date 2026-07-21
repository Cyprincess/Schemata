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

    /// <summary>
    ///     The driver receives nested selection items in its pushed-down selection and materializes the
    ///     child collections itself. Drivers without this flag receive a plan without the selection and
    ///     must include the child collection data (dictionary rows or CLR collections) in their raw
    ///     rows - suited to document or in-memory drivers whose rows naturally embed child data. A raw
    ///     row missing the nested field is a contract violation and fails.
    /// </summary>
    Nested  = 1 << 7,
    All     = Filter | Compute | Project | Order | Group | Limit | Join | Nested,
}
