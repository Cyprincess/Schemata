using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Foundation;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowSourceReadScopeShould
{
    [Fact]
    public void Suppress_Owner_And_SoftDelete_Filters_And_Restore_On_Dispose() {
        var context    = new AdviceContext(Mock.Of<System.IServiceProvider>());
        var repository = new Mock<IRepository>();
        repository.SetupGet(r => r.AdviceContext).Returns(context);

        using (FlowSourceReadScope.Enter(repository.Object)) {
            Assert.True(context.Has<QueryOwnerSuppressed>());
            Assert.True(context.Has<QuerySoftDeleteSuppressed>());
        }

        Assert.False(context.Has<QueryOwnerSuppressed>());
        Assert.False(context.Has<QuerySoftDeleteSuppressed>());
    }

    [Fact]
    public void Restore_Marker_State_After_Nested_Lifo_Scopes() {
        var context    = new AdviceContext(Mock.Of<System.IServiceProvider>());
        var repository = new Mock<IRepository>();
        repository.SetupGet(r => r.AdviceContext).Returns(context);

        using (FlowSourceReadScope.Enter(repository.Object)) {
            using (FlowSourceReadScope.Enter(repository.Object)) {
                Assert.True(context.Has<QueryOwnerSuppressed>());
                Assert.True(context.Has<QuerySoftDeleteSuppressed>());
            }

            Assert.True(context.Has<QueryOwnerSuppressed>());
            Assert.True(context.Has<QuerySoftDeleteSuppressed>());
        }

        Assert.False(context.Has<QueryOwnerSuppressed>());
        Assert.False(context.Has<QuerySoftDeleteSuppressed>());
    }

    [Fact]
    public void Tolerate_Double_Dispose() {
        var context    = new AdviceContext(Mock.Of<System.IServiceProvider>());
        var repository = new Mock<IRepository>();
        repository.SetupGet(r => r.AdviceContext).Returns(context);

        var scope = FlowSourceReadScope.Enter(repository.Object);
        scope.Dispose();
        scope.Dispose();

        Assert.False(context.Has<QueryOwnerSuppressed>());
        Assert.False(context.Has<QuerySoftDeleteSuppressed>());
    }
}
