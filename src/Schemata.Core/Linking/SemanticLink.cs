namespace Schemata.Core.Linking;

public sealed record SemanticLink(string Relation, SemanticTarget Target, string? Title = null);
