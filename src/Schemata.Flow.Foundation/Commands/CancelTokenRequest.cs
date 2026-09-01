using System.Security.Claims;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests cancellation of one token in an existing process.</summary>
/// <param name="ProcessCanonicalName">Canonical name of the token's process.</param>
/// <param name="TokenCanonicalName">Canonical name of the token to cancel.</param>
/// <param name="Principal">Caller associated with the cancellation transition.</param>
public sealed record CancelTokenRequest(
    string           ProcessCanonicalName,
    string           TokenCanonicalName,
    ClaimsPrincipal? Principal
) : ICommand<ProcessSnapshot>, IProcessScoped;
