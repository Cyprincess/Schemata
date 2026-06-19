using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Executes a long-running operation from its persisted arguments. The
///     scheduler resolves the registered handler by its stable
    ///     <see cref="OperationDescriptor.Key" /> and replays the deserialized
    ///     <typeparamref name="TArgs" />, so an operation survives a host restart
    ///     through persisted arguments.
/// </summary>
/// <typeparam name="TArgs">The serializable argument type for this operation.</typeparam>
public interface IOperationHandler<in TArgs>
    where TArgs : class
{
    /// <summary>Runs the operation and returns its result payload, or <see langword="null" />.</summary>
    Task<object?> RunAsync(TArgs args, CancellationToken ct);
}
