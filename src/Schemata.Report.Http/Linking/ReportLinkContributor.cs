using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Transport.Http.Linking;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Http.Linking;

/// <summary>
///     Contributes the report relations: a definition reaches the armed schedule job, and a snapshot
///     reaches its definition and its row-read method. The row-read method is the chunk relation: a
///     snapshot's rows are stored in chunks, and chunks have no separate HTTP surface. The snapshot
///     collection under a definition is a shared resource child-collection link.
/// </summary>
public sealed class ReportLinkContributor(ResourceRegistry registry) : ILinkContributor
{
    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        var request = context.HttpContext.Request;

        return context.Value switch {
            SchemataReport report           => Definition(request, report),
            SchemataReportSnapshot snapshot => Snapshot(request, snapshot),
            _                               => [],
        };
    }

    #endregion

    private IEnumerable<WebLink> Definition(HttpRequest request, SchemataReport report) {
        var active = report is not ISoftDelete deleted || deleted.DeleteTime is null;
        if (report.Periodic && active && Schedule(request, report) is { } schedule) {
            yield return schedule;
        }
    }

    private IEnumerable<WebLink> Snapshot(HttpRequest request, SchemataReportSnapshot snapshot) {
        var report = snapshot.Report ?? ParentLeaf(snapshot.CanonicalName);

        if (!string.IsNullOrWhiteSpace(report)
         && ResourceLinkTargets.Find(registry, typeof(SchemataReport)) is { } definition
         && ResourceLinkTargets.Instance(request, definition, report) is { } target) {
            yield return new(target, "up", Title: "report");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.CanonicalName) && Read(request, snapshot.CanonicalName!) is { } rows) {
            yield return new(rows, "related", Title: "rows");
        }
    }

    private WebLink? Schedule(HttpRequest request, SchemataReport report) {
        if (ResourceLinkTargets.FindCollection(registry, typeof(SchemataJob)) is not { } registration) {
            return null;
        }

        var filter = "key=" + JsonSerializer.Serialize(ReportJobKeyResolver.Slot(report));

        return new(ResourceLinkTargets.Collection(request, registration, filter), "related", Title: "schedule");
    }

    private string? Read(HttpRequest request, string canonicalName) {
        var registration = ResourceLinkTargets.Find(registry, typeof(SchemataReportSnapshot));
        if (registration is null
         || !registration.Methods.Any(method => method.Scope is ResourceMethodScope.Instance
                                             && method.Method is ResourceHttpMethod.Get
                                             && string.Equals(method.Verb, Verbs.Read, StringComparison.Ordinal))) {
            return null;
        }

        var path = ResourceLinkTargets.Prefix(registration) + canonicalName;

        return UriHelper.BuildRelative(request.PathBase,
                                       new PathString(LinkTargets.EncodePath(path) + ":" + Verbs.Read));
    }

    private static string? ParentLeaf(string? canonicalName) =>
        canonicalName?.Split('/') is { Length: > 2 } segments ? segments[^3] : null;
}
