using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     A persisted source registration for the database-backed catalog: the driver name and its
///     JSON-encoded driver-specific parameters.
/// </summary>
[CanonicalName("insightSources/{insight_source}")]
public class SchemataInsightSource : IIdentifier, ICanonicalName, IDescriptive, ITimestamp
{
    /// <summary>The <see cref="ISourceDriver.Name" /> that serves this source.</summary>
    public string? Driver { get; set; }

    /// <summary>The driver-specific parameters as JSON.</summary>
    public string? Params { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
