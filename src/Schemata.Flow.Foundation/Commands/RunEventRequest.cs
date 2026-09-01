using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests execution of an addressed internal Flow event.</summary>
/// <param name="ProcessCanonicalName">Canonical name of the target process.</param>
/// <param name="Token">Canonical token name addressed by the infrastructure trigger.</param>
/// <param name="Trigger">Event definition that fired.</param>
/// <param name="Payload">Typed event payload.</param>
public sealed record RunEventRequest(
    string           ProcessCanonicalName,
    string?          Token,
    IEventDefinition Trigger,
    object?          Payload
) : ICommand<ProcessSnapshot>, IProcessScoped;
