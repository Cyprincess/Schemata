using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Opts a resource into cross-collection listing with a <c>"-"</c> wildcard parent,
///     per <seealso href="https://google.aip.dev/159">AIP-159: Reading across collections</seealso>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReadAcrossAttribute : Attribute;
