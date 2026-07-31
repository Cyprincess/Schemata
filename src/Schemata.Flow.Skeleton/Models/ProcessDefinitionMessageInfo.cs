using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Wire-friendly view of a <see cref="Message" /> declared on a process definition.
/// </summary>
public sealed class ProcessDefinitionMessageInfo : IDescriptive
{
    /// <summary>Message name as declared on the definition.</summary>
    public string? Name { get; set; }

    /// <summary>Human-readable label.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Free-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }
}
