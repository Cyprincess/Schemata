using System.Collections.Generic;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Collects every <see cref="ScopedMarker" /> ever constructed, so a test can inspect one after its owning scope has been disposed.</summary>
public sealed class MarkerRegistry
{
    public List<ScopedMarker> Instances { get; } = [];
}