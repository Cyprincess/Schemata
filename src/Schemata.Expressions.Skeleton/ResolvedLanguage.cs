namespace Schemata.Expressions.Skeleton;

/// <summary>
///     The expression language selected for a request and its effective execution settings.
/// </summary>
public sealed record ResolvedLanguage(string Language, FilteringMode Filtering, int MaxResidualScanRows);
