namespace Schemata.Insight.Skeleton.Models;

/// <summary>Binds a registered source name to a request-unique alias.</summary>
/// <param name="Alias">The request-unique alias.</param>
/// <param name="Name">The registered source name resolved by the catalog.</param>
public sealed record SourceBinding(string Alias, string Name);