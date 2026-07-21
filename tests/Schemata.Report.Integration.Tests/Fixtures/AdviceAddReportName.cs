using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Integration.Tests.Fixtures;

internal sealed class AdviceAddReportName : IRepositoryAddAdvisor<SchemataReport>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                 ctx,
        IRepository<SchemataReport>   repository,
        SchemataReport                entity,
        CancellationToken             ct
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = $"report-{Identifiers.NewUid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
