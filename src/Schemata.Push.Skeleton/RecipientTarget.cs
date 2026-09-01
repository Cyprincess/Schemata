namespace Schemata.Push.Skeleton;

/// <summary>Targets a single recipient by canonical name (e.g. <c>users/chino</c>).</summary>
/// <param name="Subject">The recipient canonical name.</param>
public sealed record RecipientTarget(string Subject) : PushTarget;