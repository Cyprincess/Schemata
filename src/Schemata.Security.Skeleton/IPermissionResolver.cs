using System;

namespace Schemata.Security.Skeleton;

/// <summary>Builds permission names for entity operations.</summary>
public interface IPermissionResolver
{
    /// <summary>Resolves the permission name for an operation and entity type.</summary>
    /// <param name="operation">Operation name being authorized.</param>
    /// <param name="entity">Entity type being authorized.</param>
    /// <returns>Permission name used by authorization checks.</returns>
    string Resolve(string operation, Type entity);
}
