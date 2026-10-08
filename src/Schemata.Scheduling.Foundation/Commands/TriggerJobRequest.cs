using System;
using System.Security.Claims;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Commands;

/// <summary>Requests a one-shot execution of a registered job type.</summary>
/// <param name="JobCanonicalName">Canonical name used to serialize execution for the addressed job.</param>
/// <param name="JobType">CLR job type selected by the generic scheduler facade.</param>
/// <param name="Context">Per-fire execution context.</param>
public sealed record TriggerJobRequest(
    string     JobCanonicalName,
    Type       JobType,
    JobContext Context
) : ICommand<SchemataJobExecution>, IJobScoped, IRequestPrincipal
{
    /// <summary>The initiating caller forwarded from the dispatch envelope.</summary>
    public ClaimsPrincipal? Principal { get; set; }
}
