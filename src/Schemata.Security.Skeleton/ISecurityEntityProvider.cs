using System;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Publishes the security and token entity types selected by the registered security feature so
///     components that consume them close on the same types without repeating them in their own
///     entry points.
/// </summary>
public interface ISecurityEntityProvider
{
    /// <summary>The security entity type registered with the security feature.</summary>
    Type SecurityEntityType { get; }

    /// <summary>The token entity type registered with the security feature.</summary>
    Type TokenEntityType { get; }
}
