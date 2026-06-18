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
    ///     Dispatches the durable operation identified by <paramref name="operationKey" />
    ///     with the serializable <paramref name="args" /> and returns the pending
    ///     <see cref="Operation" /> envelope addressing it. The arguments are persisted so
    ///     the work survives a host restart; the registered <see cref="IOperationHandler{TArgs}" />
    ///     produces the result.
    /// </summary>
    /// <typeparam name="TArgs">The serializable argument type for the operation.</typeparam>
    /// <param name="operationKey">Stable key registered via <see cref="OperationDescriptor" />.</param>
    /// <param name="args">Serializable arguments persisted with the operation.</param>
    /// <param name="ct">Cancellation token for dispatch.</param>
    Task<Operation> DispatchAsync<TArgs>(
        string            operationKey,
        TArgs             args,
        CancellationToken ct
    )
        where TArgs : class;
}
