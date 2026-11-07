using System.Collections.Generic;
using System.Text.Json;
using Schemata.Abstractions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Linking;

public sealed class SchedulingLinkContributor<TJob, TExecution>(ResourceRegistry registry) : ILinkContributor
    where TJob : SchemataJob
    where TExecution : SchemataJobExecution
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (context.Target is null || context.Resource is not { } resource) yield break;
        string? subject;
        if (resource.Entity == typeof(TJob) && context.Result is TJob job) {
            if (job.CanonicalName is { Length: > 0 } name && registry.GetResource(typeof(TExecution)) is { } execution
                && ResourceNameDescriptor.ForType(execution.Entity).IsAddressable)
                yield return new(SchemataConstants.LinkRelations.Related,
                    new ResourceCollectionTarget(typeof(TExecution), Filter: "job=" + JsonSerializer.Serialize(name)), "executions");
            subject = job.Subject;
        } else if (context.Result is Operation { Metadata: { } metadata }) {
            if (metadata.Job is { Length: > 0 } owner && registry.GetResource(typeof(TJob)) is { } registration
                && ResourceNameDescriptor.ForType(registration.Entity).IsAddressable)
                yield return new(SchemataConstants.LinkRelations.Related,
                    new ResourceInstanceTarget(typeof(TJob), owner), "job");
            subject = metadata.Subject;
        } else {
            yield break;
        }

        if (subject is not { Length: > 0 }) yield break;
        foreach (var candidate in registry.Resources) {
            var descriptor = ResourceNameDescriptor.ForType(candidate.Entity);
            if (!descriptor.IsAddressable || descriptor.ParseCanonicalName(subject) is null) continue;
            yield return new(SchemataConstants.LinkRelations.Related,
                new ResourceInstanceTarget(candidate.Entity, subject), "subject");
            yield break;
        }
    }
}
