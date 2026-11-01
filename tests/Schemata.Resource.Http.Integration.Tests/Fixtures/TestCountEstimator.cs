using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Entity.EntityFrameworkCore;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

internal sealed class TestCountEstimator : IEfCoreCountEstimator<TestDbContext>
{
    public ValueTask<long?> EstimateAsync<TResult>(
        TestDbContext context,
        IQueryable<TResult> query,
        CancellationToken ct = default
    ) => ValueTask.FromResult<long?>(query.LongCount());
}
