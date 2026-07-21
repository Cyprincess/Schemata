using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Entities;

namespace Schemata.Report.Foundation;

/// <summary>Query parameters for reading one page of persisted report snapshot rows.</summary>
public sealed class ReadSnapshotRequest : ICanonicalName
{
    /// <summary>Maximum number of rows returned in the page.</summary>
    [FromQuery(Name = "page_size")]
    public int? PageSize { get; set; }

    /// <summary>Opaque continuation token returned by the preceding page.</summary>
    [FromQuery(Name = "page_token")]
    public string? PageToken { get; set; }

    string? ICanonicalName.Name { get; set; }

    string? ICanonicalName.CanonicalName { get; set; }
}
