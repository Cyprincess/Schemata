using System;
using System.Collections.Generic;

namespace Schemata.Core;

public class WellKnownOptions
{
    private readonly Dictionary<string, Delegate> _endpoints = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, Delegate> Endpoints => _endpoints;

    public void Map(string suffix, Delegate handler) {
        suffix             = suffix.Trim('/');
        _endpoints[suffix] = handler;
    }
}
