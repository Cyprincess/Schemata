namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Lets a test conditionally fail an actor's construction on demand, then flip it off to prove a later attempt can retry.</summary>
public sealed class FlakyConstructionGate
{
    public bool ShouldThrow { get; set; } = true;
}