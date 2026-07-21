using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation.Internal;

/// <summary>Scoped metadata that carries a scheduled operation into report materialization.</summary>
public sealed class ReportExecutionContext
{
    internal ReportRunKind Kind { get; set; } = ReportRunKind.ImmediatePersisted;

    internal string? Operation { get; set; }

    internal Func<CancellationToken, ValueTask<bool>>? IsCancelled { get; set; }
}
