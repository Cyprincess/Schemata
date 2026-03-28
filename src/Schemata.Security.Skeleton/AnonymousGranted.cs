namespace Schemata.Security.Skeleton;

/// <summary>Marker set in AdviceContext to signal that authorization advisors should skip access checks.</summary>
public readonly record struct AnonymousGranted;
