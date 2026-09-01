using System.Security.Claims;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests termination of an existing process and cancellation of its tokens.</summary>
/// <param name="ProcessCanonicalName">Canonical name of the process to terminate.</param>
/// <param name="Principal">Caller associated with cancellation transitions.</param>
public sealed record TerminateProcessRequest(
    string           ProcessCanonicalName,
    ClaimsPrincipal? Principal
) : ICommand<ProcessSnapshot>, IProcessScoped;
