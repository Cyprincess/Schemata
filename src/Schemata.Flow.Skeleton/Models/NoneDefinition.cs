using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A None event definition for plain Start and End events with an empty trigger payload.
/// </summary>
public sealed class NoneDefinition : IEventDefinition
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
