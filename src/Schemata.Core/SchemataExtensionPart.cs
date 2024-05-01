using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Schemata.Core;

public sealed class SchemataExtensionPart<T> : ApplicationPart, IApplicationPartTypeProvider
{
    public Assembly Assembly { get; } = typeof(T).Assembly;

    public override string Name => Assembly.GetName().Name!;

    #region IApplicationPartTypeProvider Members

    public IEnumerable<TypeInfo> Types => Assembly.DefinedTypes;

    #endregion
}
