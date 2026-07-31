using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Wire-friendly summary of a registered <see cref="ProcessDefinition" />, used as
///     the element type of <c>ListResultBase&lt;ProcessDefinitionInfo&gt;</c>. The BPMN
///     definition name is embedded in <see cref="ICanonicalName.CanonicalName" />.
/// </summary>
[CanonicalName("definitions/{definition}")]
public sealed class ProcessDefinitionInfo : ICanonicalName, IDescriptive
{
    /// <summary>Optional human-readable display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }

    /// <summary>Every element of the definition graph, including elements nested in sub-processes.</summary>
    public List<ProcessDefinitionElementInfo> Elements { get; set; } = [];

    /// <summary>Every sequence flow of the definition graph, including flows nested in sub-processes.</summary>
    public List<ProcessDefinitionFlowInfo> Flows { get; set; } = [];

    /// <summary>Message definitions declared by the process.</summary>
    public List<ProcessDefinitionMessageInfo> Messages { get; set; } = [];

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
