using System;

namespace Schemata.Messaging.RabbitMq;

/// <summary>A request type, its response type, and the wire name both travel under.</summary>
/// <param name="Name">The wire name, doubling as the routing key.</param>
/// <param name="Request">The request CLR type.</param>
/// <param name="Response">The response CLR type.</param>
public sealed record RequestBinding(string Name, Type Request, Type Response);