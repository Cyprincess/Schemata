using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Addresses a push dispatch. Each registered <see cref="IPushTransport" /> inspects the
///     concrete target type and its own subscription state to decide whether it handles a send.
///     The polymorphic annotations let a target round-trip through JSON, so a target survives the
///     durable scheduled-dispatch store and any wire exposure.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ChannelTarget), "channel")]
[JsonDerivedType(typeof(RecipientTarget), "recipient")]
[JsonDerivedType(typeof(TopicTarget), "topic")]
[JsonDerivedType(typeof(BroadcastTarget), "broadcast")]
[JsonDerivedType(typeof(CustomTarget), "custom")]
public abstract record PushTarget;

/// <summary>Targets a named channel; channel-aware transports (group/room) respond.</summary>
/// <param name="Channel">The channel identifier.</param>
public sealed record ChannelTarget(string Channel) : PushTarget;

/// <summary>Targets a single recipient by canonical name (e.g. <c>users/chino</c>).</summary>
/// <param name="Subject">The recipient canonical name.</param>
public sealed record RecipientTarget(string Subject) : PushTarget;

/// <summary>Targets a publish/subscribe topic; topic-aware transports respond.</summary>
/// <param name="Topic">The topic identifier.</param>
public sealed record TopicTarget(string Topic) : PushTarget;

/// <summary>Targets every connection a transport holds.</summary>
public sealed record BroadcastTarget : PushTarget;

/// <summary>Targets transports that recognize <paramref name="Kind" />, passing opaque parameters.</summary>
/// <param name="Kind">The custom dispatch kind a transport matches on.</param>
/// <param name="Params">Transport-specific parameters.</param>
public sealed record CustomTarget(string Kind, IReadOnlyDictionary<string, string?> Params) : PushTarget;
