using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests fan-out of a named signal to all current candidate processes.</summary>
/// <param name="SignalName">Signal definition name.</param>
/// <param name="Payload">Typed payload or serialized transport payload.</param>
/// <param name="Token">Optional canonical token name restricting each delivery.</param>
/// <param name="Principal">Caller associated with delivered transitions.</param>
public sealed record ThrowSignalRequest(
    string           SignalName,
    object?          Payload,
    string?          Token,
    ClaimsPrincipal? Principal
) : ICommand<IReadOnlyList<SignalDeliveryResult>>;
