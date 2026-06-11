using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Dispatches Resource long-running operation work without coupling Resource to a scheduler.
/// </summary>
/// <remarks>
///     Scheduling HTTP and gRPC bridges provide implementations that return canonical
///     <c>operations/{operation}</c> resource names.
/// </remarks>
public interface IOperationDispatcher
{
    /// <summary>
    ///     Dispatches <paramref name="work" /> and returns the canonical operation resource name.
    ///     The work's result becomes the operation's outcome; how it is stored and surfaced is
    ///     the dispatcher's concern.
    /// </summary>
    /// <typeparam name="TResult">The strongly-typed operation result.</typeparam>
    /// <param name="operation">Logical operation verb, for example <c>purge</c>.</param>
    /// <param name="work">Work to execute inside a dispatcher-owned service scope.</param>
    /// <param name="ct">Cancellation token for dispatch.</param>
    Task<string> DispatchAsync<TResult>(
        string                                                    operation,
        Func<IServiceProvider, CancellationToken, Task<TResult?>> work,
        CancellationToken                                         ct
    )
        where TResult : class;
}
