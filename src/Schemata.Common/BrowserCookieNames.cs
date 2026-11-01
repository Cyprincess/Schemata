using System;

namespace Schemata.Common;

/// <summary>
///     Maps logical browser cookie names to the physical names a state profile owns. The
///     prefix is fixed at construction — the host profile composes an empty prefix, a tenant
///     state profile composes its cookie prefix — so every read, write, delete, and script
///     embedded in a response resolves one physical name per logical name.
/// </summary>
public sealed class BrowserCookieNames(string prefix)
{
    private readonly string _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));

    /// <summary>Gets the physical cookie name carrying <paramref name="logicalName" /> on this profile.</summary>
    public string Get(string logicalName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        return _prefix + logicalName;
    }
}
