using System.Collections.Generic;

namespace Schemata.Push.Skeleton;

/// <summary>Targets transports that recognize <paramref name="CustomKind" />, passing opaque parameters.</summary>
/// <param name="CustomKind">The custom dispatch kind a transport matches on.</param>
/// <param name="Params">Transport-specific parameters.</param>
public sealed record CustomTarget(string CustomKind, IReadOnlyDictionary<string, string?> Params) : PushTarget;