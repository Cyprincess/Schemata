using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

internal static class AnonymousAccessHelper
{
    public static bool IsAnonymous<TEntity>(Operations operation) {
        var attribute = typeof(TEntity).GetCustomAttribute<AnonymousAttribute>();
        if (attribute is null) return false;
        return attribute.Operations is null || attribute.Operations.Contains(operation);
    }
}
