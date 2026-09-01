namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>A query, so the query pipeline chain has a payload that is not a command.</summary>
public sealed record CountWidgets : IQuery<int>;