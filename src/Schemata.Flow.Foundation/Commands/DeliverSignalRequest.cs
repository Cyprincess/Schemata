using System.Security.Claims;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests delivery of a named signal to one candidate process.</summary>
/// <param name="ProcessCanonicalName">Canonical name of the candidate process.</param>
/// <param name="SignalName">Signal definition name.</param>
/// <param name="Payload">Typed payload or serialized transport payload.</param>
/// <param name="Token">Optional canonical token name restricting the delivery.</param>
/// <param name="Principal">Caller associated with delivered transitions.</param>
public sealed record DeliverSignalRequest(
    string           ProcessCanonicalName,
    string           SignalName,
    object?          Payload,
    string?          Token,
    ClaimsPrincipal? Principal
) : ICommand<SignalDeliveryResult>, IProcessScoped;
