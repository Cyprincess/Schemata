using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Transport.Http.Linking;

namespace Schemata.Scheduling.Http.Linking;

/// <summary>
///     Contributes the Scheduling relations: a job points at its executions and an execution points
///     back at its originating job.
/// </summary>
public sealed class SchedulingLinkContributor(ResourceRegistry registry) : ILinkContributor
{
    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        if (context.Resource is not { } resource) {
            return [];
        }

        var request     = context.HttpContext.Request;
        var isJob       = typeof(SchemataJob).IsAssignableFrom(resource.Entity);
        var isExecution = typeof(SchemataJobExecution).IsAssignableFrom(resource.Entity);
        if (!isJob && !isExecution) {
            return [];
        }

        var value = context.Value;
        if (value is null) {
            return [];
        }

        if (isJob && value is SchemataJob { CanonicalName: { Length: > 0 } name } && Executions(request, name) is { } executions) {
            return [executions];
        }

        if (isExecution && value is Operation { Metadata.Job: { Length: > 0 } job }) {
            var registration = ResourceLinkTargets.Find(registry, typeof(SchemataJob));
            if (registration is not null) {
                return [new(ResourceLinkTargets.Canonical(request, registration, job), "related", Title: "job")];
            }
        }

        return [];
    }

    #endregion

    private WebLink? Executions(HttpRequest request, string job) {
        var registration = ResourceLinkTargets.FindCollection(registry, typeof(SchemataJobExecution));
        if (registration is null) {
            return null;
        }

        var filter = "job=" + JsonSerializer.Serialize(job);

        return new(ResourceLinkTargets.Collection(request, registration, filter), "related", Title: "executions");
    }
}
