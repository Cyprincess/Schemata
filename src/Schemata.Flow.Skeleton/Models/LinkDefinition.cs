using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Link event definition - a pair of Intermediate Catch and Throw events
///     connected by name. Acts as an off-page connector.
/// </summary>
public sealed class LinkDefinition : IEventDefinition
{
    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
