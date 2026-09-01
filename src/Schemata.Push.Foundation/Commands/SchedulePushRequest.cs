using System;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Foundation.Commands;

/// <summary>Requests durable scheduling of a push dispatch.</summary>
/// <param name="Context">The dispatch context carrying the message and target.</param>
/// <param name="At">When to deliver the dispatch; <see langword="null"/> means immediate.</param>
public sealed record SchedulePushRequest(
    PushContext      Context,
    DateTimeOffset? At = null
) : ICommand<Operation>;
