namespace Schemata.Push.Skeleton;

/// <summary>Targets a publish/subscribe topic; topic-aware transports respond.</summary>
/// <param name="Topic">The topic identifier.</param>
public sealed record TopicTarget(string Topic) : PushTarget;