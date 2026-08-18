namespace Schemata.Transport.RabbitMq;

/// <summary>Settings for the broker connection every RabbitMQ client in the process shares.</summary>
public class RabbitMqConnectionOptions
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

    /// <summary>Connection-establishment timeout in milliseconds.</summary>
    public int ConnectionTimeoutMs { get; set; } = 30000;
}
