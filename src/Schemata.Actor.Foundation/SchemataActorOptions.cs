using System.Collections.Generic;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation;

/// <summary>
///     Options controlling the in-process actor runtime. Lives in <c>Actor.Foundation</c> rather
///     than <c>Actor.Skeleton</c>: mailbox capacity is a runtime concern of the in-process
///     implementation, not part of the contracts package (see <c>Actor.Skeleton/AGENTS.md</c>:
///     "No runtime lives here").
/// </summary>
public class SchemataActorOptions
{
    /// <summary>
    ///     The bounded capacity of each actor's mailbox. A write beyond this capacity applies
    ///     backpressure to the writer (<see cref="System.Threading.Channels.BoundedChannelFullMode.Wait" />)
    ///     rather than growing the queue or dropping messages.
    /// </summary>
    public int MailboxCapacity { get; set; } = 1024;

    /// <summary>
    ///     Actor-type registrations staged by <see cref="SchemataActorBuilder.Register{TActor}" />,
    ///     read back once when the <see cref="IActorRegistry" /> singleton is built.
    /// </summary>
    public IList<ActorRegistration> Registrations { get; } = [];
}