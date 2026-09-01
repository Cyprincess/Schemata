using System.Collections.Generic;

namespace Schemata.Push.Skeleton;

/// <summary>Targets transports that recognize <paramref name="Kind" />, passing opaque parameters.</summary>
/// <param name="Kind">The custom dispatch kind a transport matches on.</param>
/// <param name="Params">Transport-specific parameters.</param>
public sealed record CustomTarget(string Kind, IReadOnlyDictionary<string, string?> Params) : PushTarget;