using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Cancel event definition - used within
///     <see cref="TransactionSubProcess" /> to trigger rollback.
/// </summary>
public sealed class CancelDefinition : IEventDefinition
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
