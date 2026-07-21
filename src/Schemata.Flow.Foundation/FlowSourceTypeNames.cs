using System;

namespace Schemata.Flow.Foundation;

internal static class FlowSourceTypeNames
{
    internal static string ToName(Type type) { return type.FullName ?? type.Name; }
}
