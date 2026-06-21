using System.Collections.Generic;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     The catalog-resolved binding for a source name: the driver that serves it and the
///     driver-specific parameters (e.g. the resource plural for the repository driver). Never exposed
///     on the wire.
/// </summary>
/// <param name="DriverName">The <see cref="ISourceDriver.Name" /> that serves this source.</param>
/// <param name="Params">The driver-specific parameters.</param>
public sealed record SourceConfig(string DriverName, IReadOnlyDictionary<string, object?> Params);
