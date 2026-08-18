using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace Schemata.Transport.RabbitMq;

/// <summary>Supplies the process-wide RabbitMQ connection that every client shares.</summary>
public interface IRabbitMqConnectionProvider
{
    /// <summary>
    ///     Returns the shared connection, opening it on first use. Callers create their own channels
    ///     from it and must not close or dispose the connection; the provider owns its lifetime.
    /// </summary>
    ValueTask<IConnection> GetConnectionAsync(CancellationToken ct = default);
}
