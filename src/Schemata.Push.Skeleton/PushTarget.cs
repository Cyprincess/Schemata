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