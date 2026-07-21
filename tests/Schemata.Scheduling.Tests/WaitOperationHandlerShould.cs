using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class WaitOperationHandlerShould
{
    [Fact]
    public async Task ReturnCurrentSnapshot_WhenRequestTimeoutElapses() {
        var entity = new SchemataJobExecution {
            Uid           = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CanonicalName = "operations/11111111111111111111111111111111",
            State         = ExecutionState.Running,
        };
        var operations = new Mock<IOperationService>();
        operations.Setup(s => s.WaitAsync(entity.CanonicalName, It.IsAny<CancellationToken>()))
                  .Returns(async (string _, CancellationToken ct) => {
                      await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                      return new Operation();
                  });
        operations.Setup(s => s.GetAsync(entity.CanonicalName, It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<Operation>(OperationMapper.FromExecution(entity)));
        var handler = new WaitOperationHandler(operations.Object);

        var result = await handler.InvokeAsync(entity.CanonicalName, new() { Timeout = TimeSpan.FromMilliseconds(10) },
                                               entity, null, CancellationToken.None);

        Assert.False(result.Done);
        operations.Verify(s => s.WaitAsync(entity.CanonicalName, It.IsAny<CancellationToken>()), Times.Once);
        operations.Verify(s => s.GetAsync(entity.CanonicalName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CapRequestedTimeout_AtThirtySeconds() {
        Assert.Equal(TimeSpan.FromSeconds(30), WaitOperationHandler.GetEffectiveTimeout(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(2), WaitOperationHandler.GetEffectiveTimeout(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(30), WaitOperationHandler.GetEffectiveTimeout(null));
    }
}
