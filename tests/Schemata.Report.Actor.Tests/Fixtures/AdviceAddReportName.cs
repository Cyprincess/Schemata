using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Actor.Tests.Fixtures;

/// <summary>
///     Stamps a stable <see cref="Schemata.Abstractions.Entities.ICanonicalName.Name" /> onto a report
///     whose Name is still empty, standing in for the application-supplied Name advisor that spec §5.3
///     makes the creator's responsibility.
/// </summary>
internal sealed class AdviceAddReportName : IRepositoryAddAdvisor<SchemataReport>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext               ctx,
        IRepository<SchemataReport> repository,
        SchemataReport              entity,
        CancellationToken           ct
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = $"report-{Guid.NewGuid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}