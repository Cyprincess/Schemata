using System.Collections.Immutable;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Foundation.Commands;

/// <summary>Requests fan-out push dispatch through every registered transport.</summary>
/// <param name="Context">The dispatch context carried through the request pipeline to every
/// <see cref="IPushTransport" />.</param>
public sealed record SendPushRequest(PushContext Context) : ICommand<ImmutableArray<TransportResult>>;
