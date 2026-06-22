using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class WaitOperationHandlerShould
{
    [Fact]
    public async Task ReturnCurrentSnapshot_WhenRequestTimeoutElapses() {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<SchemataJobExecution?>(new SchemataJobExecution {
                       Uid = Guid.Parse("11111111-1111-1111-1111-111111111111"), State = ExecutionState.Running,
                   }));
        var handler = new WaitOperationHandler(executions.Object);
        var entity = new SchemataJobExecution {
            Uid = Guid.Parse("11111111-1111-1111-1111-111111111111"), State = ExecutionState.Running,
        };
        var started = DateTime.UtcNow;

        var result = await handler.InvokeAsync(entity.CanonicalName, new() { Timeout = TimeSpan.FromMilliseconds(10) },
                                               entity, null, CancellationToken.None);

        Assert.False(result.Done);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CapRequestedTimeout_AtThirtySeconds() {
        Assert.Equal(TimeSpan.FromSeconds(30), WaitOperationHandler.GetEffectiveTimeout(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(2), WaitOperationHandler.GetEffectiveTimeout(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(30), WaitOperationHandler.GetEffectiveTimeout(null));
    }
}
