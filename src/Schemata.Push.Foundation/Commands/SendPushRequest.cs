using System.Collections.Immutable;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Advisors;

namespace Schemata.Push.Foundation.Commands;

/// <summary>Requests advisor-gated fan-out push dispatch through every registered transport.</summary>
/// <param name="Context">The dispatch context carried to the <see cref="IPushSendAdvisor" /> chain
/// and then to every <see cref="IPushTransport" />.</param>
public sealed record SendPushRequest(PushContext Context) : ICommand<ImmutableArray<TransportResult>>;
