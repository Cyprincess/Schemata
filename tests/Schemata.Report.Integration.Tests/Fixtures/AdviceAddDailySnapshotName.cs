using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Integration.Tests.Fixtures;

internal sealed class AdviceAddDailySnapshotName : IRepositoryAddAdvisor<SchemataReportSnapshot>
{
    public int Order => -1;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<SchemataReportSnapshot> repository, SchemataReportSnapshot entity, CancellationToken ct
    ) {
        entity.Name ??= "daily";
        return Task.FromResult(AdviseResult.Continue);
    }
}

internal sealed class AdviceAddDailyChunkName : IRepositoryAddAdvisor<SchemataReportSnapshotChunk>
{
    public int Order => -1;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<SchemataReportSnapshotChunk> repository, SchemataReportSnapshotChunk entity, CancellationToken ct
    ) {
        entity.Name ??= $"page-{entity.Index}";
        return Task.FromResult(AdviseResult.Continue);
    }
}
