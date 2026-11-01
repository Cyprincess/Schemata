using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceOperationHandlerCreateShould
{
    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_WhenDetailMappingThrows_DoesNotCommit(bool identified) {
        var request = new Student { Name = "s1" };
        var entity  = new Student { Name = "s1", CanonicalName = "students/s1" };

        var unit       = UnitOfWorkMock.Create();
        var repository = UnitOfWorkMock.Repository<Student>(unit);
        var owner      = ResourceMutationMock.Create<Student>();

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(entity);
        mapper.Setup(m => m.Map<Student, Student>(entity)).Throws(new InvalidOperationException("detail mapping failed"));

        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);
        var execution = identified ? new ResourceDispatchExecution() : null;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.CreateAsync(request, null, advice, CancellationToken.None, execution));

        owner.Verify(o => o.CreateAsync(entity, unit.Object, CancellationToken.None), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        if (execution is not null) {
            Assert.Equal(ResourceDispatchOutcome.RolledBack, execution.Outcome);
        }
    }
}
