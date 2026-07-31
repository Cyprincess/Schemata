using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Wire-friendly view of a <see cref="SequenceFlow" /> edge, referenced by element names.
///     <see cref="IsConditional" /> reports that a guard exists without disclosing the expression.
/// </summary>
public sealed class ProcessDefinitionFlowInfo : IDescriptive
{
    /// <summary>Name of the source element.</summary>
    public string? Source { get; set; }

    /// <summary>Name of the target element.</summary>
    public string? Target { get; set; }

    /// <summary>Whether this edge is the gateway fallback taken after sibling conditions fail.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether this edge carries a guard expression.</summary>
    public bool IsConditional { get; set; }

    /// <summary>Human-readable label.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Free-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }
}
