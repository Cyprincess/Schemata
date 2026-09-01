namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed record TransitionRecord(string Process, string Token, string? PreviousWaitingAtName);