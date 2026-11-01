using System.Linq;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

[Trait("Layer", "Unit")]
public class BpmnActivationShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Join_ConcurrentSubProcessActivations_ConsumesOnlyOwnBranches(bool inclusive) {
        Gateway join = inclusive ? new InclusiveGateway { Name = "join" } : new ParallelGateway { Name = "join" };
        var a = new NoneTask { Name = "a" };
        var b = new NoneTask { Name = "b" };
        var after = new NoneTask { Name = "after" };
        var sub = new EmbeddedSubProcess { Name = "sub", Children = { a, b, join, after }, ChildFlows = {
            new() { Source = a, Target = join }, new() { Source = b, Target = join }, new() { Source = join, Target = after },
        } };
        var definition = new ProcessDefinition { Name = "activations", Elements = { sub } };
        var process = Process();
        var first = Token("first", "sub", "sub", "Waiting");
        var second = Token("second", "sub", "sub", "Waiting");
        var arriving = Token("arriving", "a", null, "Active", "sub", first.CanonicalName);
        var own = Token("own", "join", "join", "Waiting", "sub", first.CanonicalName);
        var other = Token("other", "join", "join", "Waiting", "sub", second.CanonicalName);
        var upstream = Token("upstream", "b", null, "Active", "sub", second.CanonicalName);
        var result = await new BpmnEngine().AdvanceAsync(definition, process, [first, second, arriving, own, other, upstream], arriving.CanonicalName);

        Assert.Equal("Completed", own.State);
        Assert.Equal("Waiting", other.State);
        Assert.Equal("Active", upstream.State);
        Assert.Single(result.Tokens, token => token.StateName == "after" && token.State == "Active");
    }

    [Fact]
    public async Task EventGateway_FinalChild_ResumesEnclosingScope() {
        var message = new Message { Name = "finish" };
        var gateway = new EventBasedGateway { Name = "gateway" };
        var caught = new FlowEvent { Name = "caught", Position = EventPosition.IntermediateCatch, Definition = message };
        var innerEnd = new FlowEvent { Name = "inner-end", Position = EventPosition.End };
        var sub = new EmbeddedSubProcess { Name = "sub", Children = { gateway, caught, innerEnd }, ChildFlows = {
            new() { Source = gateway, Target = caught }, new() { Source = caught, Target = innerEnd },
        } };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = "event-gateway", Elements = { sub, end }, Flows = { new() { Source = sub, Target = end } } };
        var parent = Token("parent", "sub", "sub", "Waiting");
        var child = Token("child", "gateway", "gateway", "Waiting", "sub", parent.CanonicalName);

        var snapshot = await new BpmnEngine().TriggerAsync(definition, Process(), [parent, child], message, null, child.CanonicalName);

        Assert.Equal("Completed", snapshot.Process.State);
        Assert.Equal("end", parent.StateName);
        Assert.All(snapshot.Tokens, token => Assert.Equal("Completed", token.State));
    }

    [Fact]
    public async Task FinalCatch_ResumesEveryWaitingAncestor() {
        var message = new Message { Name = "finish" };
        var wait = new FlowEvent { Name = "wait", Position = EventPosition.IntermediateCatch, Definition = message };
        var innerEnd = new FlowEvent { Name = "inner-end", Position = EventPosition.End };
        var inner = new EmbeddedSubProcess { Name = "inner", Children = { wait, innerEnd }, ChildFlows = { new() { Source = wait, Target = innerEnd } } };
        var outerEnd = new FlowEvent { Name = "outer-end", Position = EventPosition.End };
        var outer = new EmbeddedSubProcess { Name = "outer", Children = { inner, outerEnd }, ChildFlows = { new() { Source = inner, Target = outerEnd } } };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = "nested", Elements = { outer, end }, Flows = { new() { Source = outer, Target = end } } };
        var root = Token("root", "outer", "outer", "Waiting");
        var parent = Token("parent", "inner", "inner", "Waiting", "outer", root.CanonicalName);
        var child = Token("child", "wait", "wait", "Waiting", "inner", parent.CanonicalName);

        var result = await new BpmnEngine().TriggerAsync(definition, Process(), [root, parent, child], message, null, child.CanonicalName);

        Assert.Equal("Completed", result.Process.State);
        Assert.Equal("end", root.StateName);
        Assert.Equal("outer-end", parent.StateName);
        Assert.All(result.Tokens, token => Assert.Equal("Completed", token.State));
    }

    [Fact]
    public async Task GatewayIntoSubProcess_SpawnsChildrenWhenProcessNameMatchesScope() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new ExclusiveGateway { Name = "route" };
        var innerStart = new FlowEvent { Name = "inner-start", Position = EventPosition.Start };
        var task = new NoneTask { Name = "task" };
        var innerEnd = new FlowEvent { Name = "inner-end", Position = EventPosition.End };
        var sub = new EmbeddedSubProcess { Name = "p1", Children = { innerStart, task, innerEnd }, ChildFlows = {
            new() { Source = innerStart, Target = task }, new() { Source = task, Target = innerEnd },
        } };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = "collision", Elements = { start, gateway, sub, end }, Flows = {
            new() { Source = start, Target = gateway }, new() { Source = gateway, Target = sub }, new() { Source = sub, Target = end },
        } };
        var engine = new BpmnEngine();
        var result = await engine.StartAsync(definition, Process());
        var child = Assert.Single(result.Tokens, token => token.StateName == "task");
        var parent = Assert.Single(result.Tokens, token => token.WaitingAtName == "p1");
        Assert.Equal(string.Empty, parent.ScopeName);
        Assert.Equal(parent.CanonicalName, child.Spawner);
        result = await engine.AdvanceAsync(definition, result.Process, result.Tokens, child.CanonicalName);
        Assert.Equal("Completed", result.Process.State);
        Assert.Equal("end", parent.StateName);
    }

    [Fact]
    public async Task Terminate_CancelsNestedDescendantsOfOnlyCurrentActivation() {
        var task = new NoneTask { Name = "task" };
        var terminate = new FlowEvent { Name = "terminate", Position = EventPosition.End, IsTerminate = true };
        var nested = new EmbeddedSubProcess { Name = "nested" };
        var sub = new EmbeddedSubProcess { Name = "sub", Children = { task, terminate, nested }, ChildFlows = { new() { Source = task, Target = terminate } } };
        var after = new NoneTask { Name = "after" };
        var definition = new ProcessDefinition { Name = "terminate", Elements = { sub, after }, Flows = { new() { Source = sub, Target = after } } };
        var parent = Token("parent", "sub", "sub", "Waiting");
        var otherParent = Token("other-parent", "sub", "sub", "Waiting");
        var trigger = Token("trigger", "task", null, "Active", "sub", parent.CanonicalName);
        var nestedParent = Token("nested-parent", "nested", "nested", "Waiting", "sub", parent.CanonicalName);
        var descendant = Token("descendant", "nested-task", null, "Active", "nested", nestedParent.CanonicalName);
        var other = Token("other", "task", null, "Active", "sub", otherParent.CanonicalName);

        await new BpmnEngine().AdvanceAsync(definition, Process(), [parent, otherParent, trigger, nestedParent, descendant, other], trigger.CanonicalName);

        Assert.Equal("Cancelled", nestedParent.State);
        Assert.Equal("Cancelled", descendant.State);
        Assert.Equal("after", parent.StateName);
        Assert.Equal("Active", other.State);
        Assert.Equal("Waiting", otherParent.State);
    }

    [Fact]
    public async Task TransactionCancel_CancelsNestedDescendantsOnlyInCurrentActivation() {
        var task = new NoneTask { Name = "task" };
        var cancel = new FlowEvent { Name = "cancel", Position = EventPosition.End, Definition = new CancelDefinition { Name = "Cancel" } };
        var nested = new EmbeddedSubProcess { Name = "nested" };
        var transaction = new TransactionSubProcess { Name = "transaction", Children = { task, cancel, nested }, ChildFlows = { new() { Source = task, Target = cancel } } };
        var after = new NoneTask { Name = "after" };
        var definition = new ProcessDefinition { Name = "transaction", Elements = { transaction, after }, Flows = { new() { Source = transaction, Target = after } } };
        var parent = Token("parent", "transaction", "transaction", "Waiting");
        var otherParent = Token("other-parent", "transaction", "transaction", "Waiting");
        var trigger = Token("trigger", "task", null, "Active", "transaction", parent.CanonicalName);
        var nestedParent = Token("nested-parent", "nested", "nested", "Waiting", "transaction", parent.CanonicalName);
        var descendant = Token("descendant", "nested-task", null, "Active", "nested", nestedParent.CanonicalName);
        var other = Token("other", "task", null, "Active", "transaction", otherParent.CanonicalName);

        await new BpmnEngine().AdvanceAsync(definition, Process(), [parent, otherParent, trigger, nestedParent, descendant, other], trigger.CanonicalName);

        Assert.Equal("Cancelled", nestedParent.State);
        Assert.Equal("Cancelled", descendant.State);
        Assert.Equal("after", parent.StateName);
        Assert.Equal("Active", other.State);
        Assert.Equal("Waiting", otherParent.State);
    }

    [Fact]
    public async Task SingleFalseActivityGuard_LeavesTokenAtActivity() {
        var task = new NoneTask { Name = "task" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = "guard", Elements = { task, end }, Flows = {
            new() { Source = task, Target = end, Condition = new LambdaConditionExpression { Lambda = _ => new(false) } },
        } };
        var token = Token("token", "task", null, "Active");

        var snapshot = await new BpmnEngine().AdvanceAsync(definition, Process(), [token]);

        Assert.Equal("task", token.StateName);
        Assert.Equal("Active", token.State);
        Assert.Empty(snapshot.Transitions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildScopeExit_DoesNotWaitForOrCancelExternalBoundaryBranch(bool terminate) {
        var task = new NoneTask { Name = "task" };
        var end = new FlowEvent { Name = "inner-end", Position = EventPosition.End, IsTerminate = terminate };
        var sub = new EmbeddedSubProcess { Name = "sub", Children = { task, end }, ChildFlows = { new() { Source = task, Target = end } } };
        var after = new NoneTask { Name = "after" };
        var external = new NoneTask { Name = "boundary-handler" };
        var definition = new ProcessDefinition { Name = "external-branch", Elements = { sub, after, external }, Flows = { new() { Source = sub, Target = after } } };
        var parent = Token("parent", "sub", "sub", "Waiting");
        var child = Token("child", "task", null, "Active", "sub", parent.CanonicalName);
        var branch = Token("branch", "boundary-handler", null, "Active", "", parent.CanonicalName);

        await new BpmnEngine().AdvanceAsync(definition, Process(), [parent, child, branch], child.CanonicalName);

        Assert.Equal("after", parent.StateName);
        Assert.Equal("Active", parent.State);
        Assert.Equal("Active", branch.State);
        Assert.Equal("boundary-handler", branch.StateName);
    }


    private static SchemataProcess Process() => new() { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "test" };
    private static SchemataProcessToken Token(string name, string state, string? waiting, string status, string scope = "", string? spawner = null) => new() {
        Name = name, CanonicalName = $"processes/p1/tokens/{name}", Process = "p1", ScopeName = scope,
        StateName = state, WaitingAtName = waiting, State = status, Spawner = spawner,
    };
}
