using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Opts a resource into AIP-159 read-across behavior, allowing listing across parent boundaries with a "-" wildcard.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReadAcrossAttribute : Attribute;
