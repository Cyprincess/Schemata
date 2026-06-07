using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Schemata.Core;

namespace Schemata.Transport.Http;

/// <summary>
///     <see cref="IApplicationPartConfigurator" /> that exposes the assembly of
///     <typeparamref name="T" /> as a <see cref="SchemataExtensionPart{T}" />.
/// </summary>
internal sealed class ApplicationPartConfigurator<T> : IApplicationPartConfigurator
{
    private readonly SchemataExtensionPart<T> _part = new();

    /// <summary>The part name, used by the caller for idempotency checks.</summary>
    public string PartName => _part.Name;

    #region IApplicationPartConfigurator Members

    public void Configure(ApplicationPartManager manager) {
        manager.ApplicationParts.Add(_part);
    }

    #endregion
}
