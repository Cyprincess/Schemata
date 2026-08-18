namespace Schemata.Event.RabbitMq;

/// <summary>
///     Topology settings for the RabbitMQ event bus and consumer host. Broker connection settings
///     live on <c>RabbitMqConnectionOptions</c> in <c>Schemata.Transport.RabbitMq</c>, which owns the
///     connection every client in the process shares.
/// </summary>
public class RabbitMqEventOptions
{
    /// <summary>Exchange that publishers write to and consumers bind against.</summary>
    public string ExchangeName { get; set; } = "schemata.events";

    /// <summary>Exchange type (<c>topic</c>, <c>direct</c>, <c>fanout</c>, <c>headers</c>).</summary>
    public string ExchangeType { get; set; } = "topic";

    /// <summary>Queue the consumer host declares and binds to <see cref="ExchangeName"/>.</summary>
    public string QueueName { get; set; } = "schemata.consumer";

    /// <summary>Request/response wait-for-reply timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    ///     Number of messages the broker may deliver before the consumer must acknowledge.
    ///     Provides backpressure and prevents a slow handler from starving other consumers.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>
    ///     Dead-letter exchange routed to when a handler throws, the message references an
    ///     unregistered event type, or deserialization fails. Empty values reject poison messages
    ///     with re-queue disabled.
    /// </summary>
    public string DeadLetterExchange { get; set; } = "schemata.events.dlx";

    /// <summary>Optional routing key for the dead-letter exchange. Empty preserves the original routing key.</summary>
    public string DeadLetterRoutingKey { get; set; } = string.Empty;
}
