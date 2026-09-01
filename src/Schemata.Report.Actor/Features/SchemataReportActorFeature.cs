using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Features;
using Schemata.Actor.Foundation.Internal;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Actor.Internal;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Actor.Features;

/// <summary>
///     Installs the Report.Actor bridge: replaces the unkeyed default handler of both report
///     generation commands with <see cref="ActorSerializingHandler{TRequest,TResult}" /> and
///     registers the shared <see cref="RequestDispatchingActor" /> under the <c>"report"</c> route
///     keyed by report name, so every entry point that resolves the unkeyed handler — facade,
///     dispatcher, scheduled job, HTTP/gRPC <c>:generate</c> — serializes concurrent generations of
///     the same report.
/// </summary>
/// <remarks>
///     An inline request (no report name) has no report identity to key on and is left unwrapped by
///     the handler itself: it resolves the keyed default handler directly, exactly as without the
///     bridge.
/// </remarks>
[DependsOn<SchemataActorFeature>]
[DependsOn(typeof(SchemataReportFeature<,,>))]
public sealed class SchemataReportActorFeature<TReport, TSnapshot, TChunk> : FeatureBase
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Report.Actor feature.</summary>
    public const int DefaultPriority = SchemataReportFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>.DefaultPriority + 600_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.Replace(ServiceDescriptor.Scoped<
            IRequestHandler<RunReportRequest, ReportResult>,
            ActorSerializingHandler<RunReportRequest, ReportResult>>());
        services.Replace(ServiceDescriptor.Scoped<
            IRequestHandler<GenerateReportRequest, Operation>,
            ActorSerializingHandler<GenerateReportRequest, Operation>>());

        new SchemataActorBuilder(schemata, services).Register<RequestDispatchingActor>(
            "report", ReportConstants.Handlers.Default);
    }
}