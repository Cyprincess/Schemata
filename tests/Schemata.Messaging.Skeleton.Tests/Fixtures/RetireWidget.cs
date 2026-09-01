namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>A command with no result, exercising the <see cref="Schemata.Abstractions.Unit" /> path.</summary>
public sealed record RetireWidget(string Name) : ICommand;