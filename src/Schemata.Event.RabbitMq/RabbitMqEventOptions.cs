namespace Schemata.Event.RabbitMq;

/// <summary>Connection and topology settings for the RabbitMQ event bus and consumer host.</summary>
public class RabbitMqEventOptions
{
    /// <summary>Broker host name or IP. Defaults to <c>localhost</c>.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>Broker AMQP port. Defaults to 5672.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>SASL PLAIN user name. Defaults to <c>guest</c>.</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>SASL PLAIN password. Defaults to <c>guest</c>.</summary>
    public string Password { get; set; } = "guest";

    /// <summary>AMQP virtual host the connection joins. Defaults to <c>/</c>.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Exchange that publishers write to and consumers bind against.</summary>
    public string ExchangeName { get; set; } = "schemata.events";

    /// <summary>Exchange type (<c>topic</c>, <c>direct</c>, <c>fanout</c>, <c>headers</c>).</summary>
    public string ExchangeType { get; set; } = "topic";

    /// <summary>Queue the consumer host declares and binds to <see cref="ExchangeName"/>.</summary>
    public string QueueName { get; set; } = "schemata.consumer";

    /// <summary>Connection-establishment timeout in milliseconds.</summary>
    public int ConnectionTimeoutMs { get; set; } = 30000;

    /// <summary>Request/response wait-for-reply timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    ///     Number of messages the broker may deliver before the consumer must acknowledge.
    ///     Provides backpressure and prevents a slow handler from starving other consumers.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>
    ///     Dead-letter exchange routed to when a handler throws, the message references an
    ///     unregistered event type, or deserialization fails. Leave empty to disable DLX routing
    ///     (poison messages will then be rejected without re-queue and lost).
    /// </summary>
    public string DeadLetterExchange { get; set; } = "schemata.events.dlx";

    /// <summary>Optional routing key for the dead-letter exchange. Empty preserves the original routing key.</summary>
    public string DeadLetterRoutingKey { get; set; } = string.Empty;
}
