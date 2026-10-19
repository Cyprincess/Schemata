namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>A request whose reply proves a normal receive ran.</summary>
public sealed record AskEcho : Messaging.Skeleton.IRequest<string>;