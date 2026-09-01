using System.Security.Claims;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests correlation of a named message to an existing process.</summary>
/// <param name="ProcessCanonicalName">Canonical name of the target process.</param>
/// <param name="MessageName">Message definition name.</param>
/// <param name="Payload">Typed payload or serialized transport payload.</param>
/// <param name="Token">Canonical token name, or <see langword="null" /> when the engine must select it.</param>
/// <param name="Principal">Caller associated with the transition.</param>
public sealed record CorrelateMessageRequest(
    string           ProcessCanonicalName,
    string           MessageName,
    object?          Payload,
    string?          Token,
    ClaimsPrincipal? Principal
) : ICommand<ProcessSnapshot>, IProcessScoped;
