using System;

namespace Schemata.Workflow.Skeleton;

public interface ITypeResolver
{
    Type ResolveType(string? name);

    bool TryResolveType(string? name, out Type? type);
}
