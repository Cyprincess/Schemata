using System;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Skeleton;

/// <summary>A single mailbox item: a message payload together with its sender and metadata.</summary>
/// <param name="Payload">The message being delivered.</param>
/// <param name="Sender">
///     The reference to reply to, or <see langword="null" /> when the message was delivered
///     without a sender.
/// </param>
/// <param name="Context">
///     The sender-captured ambient state to restore in the target turn's scope before the
///     handler runs, or <see langword="null" /> when there is none to propagate.
/// </param>
/// <param name="CorrelationId">
///     The pending-reply key for an <c>Ask</c>, or <see cref="Guid.Empty" /> for a <c>Tell</c>
///     that expects no reply.
/// </param>
public sealed record Envelope(
    IMessage        Payload,
    IActorRef?      Sender        = null,
    MessageContext? Context       = null,
    Guid            CorrelationId = default);
