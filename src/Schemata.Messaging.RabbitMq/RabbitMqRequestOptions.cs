using System;
using System.Collections.Generic;
using Schemata.Messaging.Skeleton;

namespace Schemata.Messaging.RabbitMq;

/// <summary>Topology and wire-name configuration for the RabbitMQ request dispatcher.</summary>
public class RabbitMqRequestOptions
{
    private readonly Dictionary<string, RequestBinding> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, RequestBinding>   _byType = [];

    /// <summary>Exchange the requests and replies are routed through.</summary>
    public string ExchangeName { get; set; } = "schemata.requests";

    /// <summary>Exchange type. Direct routing keyed by the registered wire name.</summary>
    public string ExchangeType { get; set; } = "direct";

    /// <summary>Queue the consumer side reads requests from. Empty disables the consumer.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>How long a caller waits for a reply before the request fails.</summary>
    public int RequestTimeoutMs { get; set; } = 30_000;

    /// <summary>The registered bindings, keyed by wire name.</summary>
    public IReadOnlyDictionary<string, RequestBinding> Bindings => _byName;

    /// <summary>
    ///     Registers the wire name for a request type and its response.
    /// </summary>
    /// <remarks>
    ///     Registration is mandatory: a CLR type name never travels on the wire, because renaming a
    ///     class or moving it between assemblies would silently break every already-deployed peer.
    ///     This mirrors the event bus's <c>RegisterEvent&lt;T&gt;(name)</c> rule.
    /// </remarks>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="name">The wire name, doubling as the routing key.</param>
    public RabbitMqRequestOptions Register<TRequest, TResponse>(string name)
        where TRequest : IRequest<TResponse> {
        var binding = new RequestBinding(name, typeof(TRequest), typeof(TResponse));

        _byName[name]             = binding;
        _byType[typeof(TRequest)] = binding;

        return this;
    }

    /// <summary>Returns the binding registered for <paramref name="request" />.</summary>
    /// <exception cref="InvalidOperationException">The request type was never registered.</exception>
    public RequestBinding Require(Type request) {
        if (_byType.TryGetValue(request, out var binding)) {
            return binding;
        }

        throw new InvalidOperationException($"No RabbitMQ wire name registered for request type '{
            request.FullName
        }'. Call Register<TRequest, TResponse>(name) when adding the dispatcher.");
    }

    /// <summary>Returns the binding registered under <paramref name="name" />, if any.</summary>
    public RequestBinding? Resolve(string name) {
        return _byName.GetValueOrDefault(name);
    }
}

/// <summary>A request type, its response type, and the wire name both travel under.</summary>
/// <param name="Name">The wire name, doubling as the routing key.</param>
/// <param name="Request">The request CLR type.</param>
/// <param name="Response">The response CLR type.</param>
public sealed record RequestBinding(string Name, Type Request, Type Response);
