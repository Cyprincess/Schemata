using System.Linq;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class NestedEnterTaskShould
{
    [Fact]
    public void Insert_Enter_Task_And_Rewrite_Only_Owning_SubProcess_Scope() {
        var definition = new NestedEnterTaskProcess();
        var enterTask  = Assert.Single(definition.Scope.Children.OfType<ProcedureTask>());

        Assert.Equal("Enter_NestedTarget", enterTask.Name);
        Assert.DoesNotContain(definition.Elements, element => element is ProcedureTask);
        Assert.Single(definition.Flows);
        Assert.Same(definition.NestedTarget, definition.Flows[0].Target);

        Assert.Same(enterTask, definition.Scope.ChildFlows.Single(flow => flow.Source == definition.NestedSource).Target);
        Assert.Same(definition.NestedTarget, definition.Scope.ChildFlows.Single(flow => flow.Source == enterTask).Target);
    }

    private sealed class NestedEnterTaskProcess : ProcessDefinition
    {
        public NestedEnterTaskProcess() {
            Elements.Remove(NestedSource);
            Elements.Remove(NestedTarget);
            Scope.Children.Add(NestedSource);
            Scope.Children.Add(NestedTarget);
            Scope.ChildFlows.Add(new() { Source = NestedSource, Target = NestedTarget });
            Flows.Add(new() { Source = RootSource, Target = NestedTarget });

            this.During(NestedTarget).OnEnter(_ => ValueTask.CompletedTask);
        }

        public UserTask           RootSource   { get; } = null!;
        public EmbeddedSubProcess Scope        { get; } = null!;
        public UserTask           NestedSource { get; } = null!;
        public UserTask           NestedTarget { get; } = null!;
    }
}
