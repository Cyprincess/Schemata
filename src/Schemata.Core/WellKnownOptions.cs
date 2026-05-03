using System;
using System.Collections.Generic;

namespace Schemata.Core;

/// <summary>
///     Holds a registry of well-known endpoint suffixes mapped to their request
///     delegates. Used by <see cref="Features.SchemataWellKnownFeature" /> to serve
///     <c>/.well-known/</c> routes.
/// </summary>
public class WellKnownOptions
{
    private readonly Dictionary<string, Delegate> _endpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The registered well-known endpoint suffix-to-handler mappings.
    /// </summary>
    public IReadOnlyDictionary<string, Delegate> Endpoints => _endpoints;

    /// <summary>
    ///     Registers a well-known path suffix (e.g. <c>"change-password"</c>) and its
    ///     handler delegate. Leading and trailing slashes on <paramref name="suffix" />
    ///     are stripped.
    /// </summary>
    /// <param name="suffix">The well-known URI suffix.</param>
    /// <param name="handler">The request handler delegate.</param>
    public void Map(string suffix, Delegate handler) {
        suffix             = suffix.Trim('/');
        _endpoints[suffix] = handler;
    }
}
