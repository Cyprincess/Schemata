using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton;

/// <summary>Dispatches commands to their handlers, running the command advisor chain around each.</summary>
/// <remarks>
///     A distinct service type from <see cref="IQueryDispatcher" /> so the write path can be
///     replaced — with a distributed dispatcher, for instance — without touching the read path.
/// </remarks>
public interface ICommandDispatcher : IRequestDispatcher
{
    /// <summary>Dispatches a result-less <paramref name="command" /> to its single handler.</summary>
    /// <remarks>
    ///     Present so a call site does not have to name
    ///     <see cref="Schemata.Abstractions.Unit" /> to send a command that returns nothing.
    /// </remarks>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">The command instance.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand;
}
