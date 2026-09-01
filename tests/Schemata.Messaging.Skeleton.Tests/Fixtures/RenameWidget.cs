namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>A command carrying a result, so the dispatcher's return path is observable.</summary>
public sealed record RenameWidget(string Name) : ICommand<string>;