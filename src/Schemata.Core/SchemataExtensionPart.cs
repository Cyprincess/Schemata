using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Schemata.Core;

/// <summary>
///     An MVC application part that exposes the assembly of <typeparamref name="T" /> for controller discovery.
/// </summary>
/// <typeparam name="T">A type whose assembly should be registered as an application part.</typeparam>
public sealed class SchemataExtensionPart<T> : ApplicationPart, IApplicationPartTypeProvider
{
    /// <summary>
    ///     Gets the assembly containing type <typeparamref name="T" />.
    /// </summary>
    public Assembly Assembly { get; } = typeof(T).Assembly;

    /// <inheritdoc />
    public override string Name => Assembly.GetName().Name!;

    #region IApplicationPartTypeProvider Members

    /// <inheritdoc />
    public IEnumerable<TypeInfo> Types => Assembly.DefinedTypes;

    #endregion
}
