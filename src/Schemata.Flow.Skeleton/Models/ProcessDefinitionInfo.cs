using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Wire-friendly summary of a registered <see cref="ProcessDefinition" />, used as
///     the element type of <c>ListResultBase&lt;ProcessDefinitionInfo&gt;</c>. The BPMN
///     definition name is embedded in <see cref="ICanonicalName.CanonicalName" /> rather
///     than surfaced through a separate field.
/// </summary>
[DisplayName("Definition")]
[CanonicalName("definitions/{definition}")]
public sealed class ProcessDefinitionInfo : ICanonicalName
{
    /// <summary>Optional human-readable display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
