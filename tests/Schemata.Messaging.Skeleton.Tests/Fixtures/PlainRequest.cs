namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>A plain request that is neither a command nor a query, so no pipeline chain runs for it.</summary>
public sealed record PlainRequest(string Value) : IRequest<string>;