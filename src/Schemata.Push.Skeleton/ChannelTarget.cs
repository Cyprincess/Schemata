namespace Schemata.Push.Skeleton;

/// <summary>Targets a named channel; channel-aware transports (group/room) respond.</summary>
/// <param name="Channel">The channel identifier.</param>
public sealed record ChannelTarget(string Channel) : PushTarget;