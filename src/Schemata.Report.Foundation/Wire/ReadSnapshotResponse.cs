using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Report.Foundation;

/// <summary>One page of rows from a persisted report snapshot.</summary>
public sealed class ReadSnapshotResponse : ICanonicalName
{
    /// <summary>Rows decoded from the snapshot chunks needed for this page.</summary>
    public IList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];

    /// <summary>Opaque token for the next page, or <see langword="null" /> after the final row.</summary>
    public string? NextPageToken { get; set; }

    string? ICanonicalName.Name { get; set; }

    string? ICanonicalName.CanonicalName { get; set; }
}
