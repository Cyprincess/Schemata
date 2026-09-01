namespace Schemata.Report.Tests.Fixtures;

internal sealed record ReportChunkMetadata(
    string  Name,
    int     Index,
    string  CanonicalName,
    string? Report,
    string? Snapshot,
    int     RowCount);