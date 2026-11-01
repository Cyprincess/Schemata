using Schemata.Abstractions.Entities;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Integration.Tests.Fixtures;

[CanonicalName("reports/{report}")]
public sealed class ConflictingReport : SchemataReport;
