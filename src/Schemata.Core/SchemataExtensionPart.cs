using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Schemata.Core;

public sealed class SchemataExtensionPart<T> : ApplicationPart, IApplicationPartTypeProvider
{
    public SchemataExtensionPart() {
        Assembly = typeof(T).Assembly;
    }

    public Assembly Assembly { get; }

    public override string Name => Assembly.GetName().Name!;

    #region IApplicationPartTypeProvider Members

    public IEnumerable<TypeInfo> Types => Assembly.DefinedTypes;

    #endregion
}
