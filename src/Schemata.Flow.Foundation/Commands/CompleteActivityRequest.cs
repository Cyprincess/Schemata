using System.Security.Claims;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests completion of an activity token in an existing process.</summary>
/// <param name="ProcessCanonicalName">Canonical name of the process to advance.</param>
/// <param name="Token">Canonical token name, or <see langword="null" /> when the engine must select it.</param>
/// <param name="Principal">Caller associated with the transition.</param>
public sealed record CompleteActivityRequest(
    string           ProcessCanonicalName,
    string?          Token,
    ClaimsPrincipal? Principal
) : ICommand<ProcessSnapshot>, IProcessScoped;
