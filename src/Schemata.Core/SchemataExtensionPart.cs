using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Schemata.Core;

/// <summary>
///     An <see cref="ApplicationPart" /> that exposes the assembly of
///     <typeparamref name="T" /> for controller discovery without requiring an
///     explicit <c>[assembly: ApplicationPart(...)]</c> attribute.
/// </summary>
/// <typeparam name="T">Any type whose assembly contains controllers to register.</typeparam>
public sealed class SchemataExtensionPart<T> : ApplicationPart, IApplicationPartTypeProvider
{
    /// <summary>
    ///     The assembly containing controllers to register, derived from
    ///     <typeparamref name="T" />.
    /// </summary>
    public Assembly Assembly { get; } = typeof(T).Assembly;

    public override string Name => Assembly.GetName().Name!;

    #region IApplicationPartTypeProvider Members

    public IEnumerable<TypeInfo> Types => Assembly.DefinedTypes;

    #endregion
}
