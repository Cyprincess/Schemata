using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Xunit;

namespace Schemata.Abstractions.Tests.Advisors;

public class AdviceContextAmbientShould
{
    [Fact]
    public void Return_Null_When_Nothing_Established()
        => Assert.Null(AdviceContext.Current);

    [Fact]
    public void Expose_Context_Within_Scope() {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        using (AdviceContext.Establish(ctx)) {
            Assert.Same(ctx, AdviceContext.Current);
        }
        Assert.Null(AdviceContext.Current);
    }

    [Fact]
    public void Restore_Outer_After_Nested_Scope() {
        var sp    = new ServiceCollection().BuildServiceProvider();
        var outer = new AdviceContext(sp);
        var inner = new AdviceContext(sp);
        using (AdviceContext.Establish(outer)) {
            using (AdviceContext.Establish(inner)) {
                Assert.Same(inner, AdviceContext.Current);
            }
            Assert.Same(outer, AdviceContext.Current);
        }
        Assert.Null(AdviceContext.Current);
    }

    [Fact]
    public async Task Flow_Through_Await() {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        using var _ = AdviceContext.Establish(ctx);
        await Task.Yield();
        Assert.Same(ctx, AdviceContext.Current);
    }

    [Fact]
    public void Not_Overwrite_Later_Context_When_Scope_Disposed_Twice() {
        var sp    = new ServiceCollection().BuildServiceProvider();
        var outer = new AdviceContext(sp);
        var inner = new AdviceContext(sp);
        var third = new AdviceContext(sp);

        using (AdviceContext.Establish(outer)) {
            var innerScope = AdviceContext.Establish(inner);
            innerScope.Dispose();
            innerScope.Dispose();
            Assert.Same(outer, AdviceContext.Current);

            using (AdviceContext.Establish(third)) {
                // A stale disposal of the already-disposed inner scope must not restore the
                // outer context over the later-established third context.
                innerScope.Dispose();
                Assert.Same(third, AdviceContext.Current);
            }

            Assert.Same(outer, AdviceContext.Current);
        }
        Assert.Null(AdviceContext.Current);
    }
}
