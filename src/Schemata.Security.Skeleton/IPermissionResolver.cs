using System;

namespace Schemata.Security.Skeleton;

public interface IPermissionResolver
{
    string Resolve(string operation, Type entity);
}
