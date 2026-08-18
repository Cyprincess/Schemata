using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Schemata.Transport.RabbitMq.Internal;

/// <summary>Opens the broker connection once and hands the same instance to every caller.</summary>
public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim                       _initializationLock = new(1, 1);
    private readonly IOptions<RabbitMqConnectionOptions> _options;
    private          IConnection?                        _connection;

    /// <summary>Initializes a provider that connects lazily on the first request.</summary>
    public RabbitMqConnectionProvider(IOptions<RabbitMqConnectionOptions> options) {
        _options = options;
    }

    #region IAsyncDisposable Members

    public async ValueTask DisposeAsync() {
        await _initializationLock.WaitAsync();
        try {
            if (_connection is { } connection) {
                _connection = null;
                await connection.DisposeAsync();
            }
        } finally {
            _initializationLock.Release();
        }
    }

    #endregion

    #region IRabbitMqConnectionProvider Members

    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken ct = default) {
        if (_connection is { } existingConnection) {
            return existingConnection;
        }

        await _initializationLock.WaitAsync(ct);
        try {
            if (_connection is { } initializedConnection) {
                return initializedConnection;
            }

            var options = _options.Value;
            var factory = new ConnectionFactory {
                HostName                   = options.HostName,
                Port                       = options.Port,
                UserName                   = options.UserName,
                Password                   = options.Password,
                VirtualHost                = options.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromMilliseconds(options.ConnectionTimeoutMs),
            };

            var newConnection = await factory.CreateConnectionAsync(ct);
            _connection = newConnection;
            return newConnection;
        } finally {
            _initializationLock.Release();
        }
    }

    #endregion
}
