using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Base type of every BPMN element placed inside a <see cref="ProcessDefinition" />.
///     <see cref="Name" /> is the element's identity; the <see cref="IDescriptive" /> members carry
///     the labels a renderer shows, and editing them never moves a token.
/// </summary>
public abstract class FlowElement : IDescriptive
{
    /// <summary>
    ///     Canonical element name: the element's identity within its process definition.
    ///     Unique across the definition and deterministic across definition rebuilds, so it is
    ///     the resume key persisted on process tokens. Audit rows and error payloads surface it
    ///     as the element label.
    /// </summary>
    public string Name { get; set; } = null!;

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
