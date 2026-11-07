using System.Collections.Generic;
using System.Text.Json;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Core.Linking;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation.Linking;

public sealed class ReportLinkContributor : ILinkContributor
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (context.Target is null) yield break;
        if (context.Result is SchemataReport report && report.Periodic
         && (report is not ISoftDelete deleted || deleted.DeleteTime is null))
            yield return new(LinkRelations.Related, new ResourceCollectionTarget(typeof(SchemataJob),
                Filter: "key=" + JsonSerializer.Serialize(ReportJobKeyResolver.Slot(report))), "schedule");
        if (context.Result is SchemataReportSnapshot { CanonicalName: { Length: > 0 } name } snapshot) {
            var descriptor = ResourceNameDescriptor.ForType(context.Resource?.Entity ?? snapshot.GetType());
            if (descriptor.ParentOf(name) is null) yield break;
            yield return new(LinkRelations.Related,
                new ResourceInstanceTarget(typeof(SchemataReportSnapshot), name, Verbs.Read), "rows");
        }
    }
}
