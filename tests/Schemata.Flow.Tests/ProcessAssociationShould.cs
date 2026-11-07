using System;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

[Trait("Category", "Unit")]
public sealed class ProcessAssociationShould
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Reject_Unaddressed_Process_In_Every_Row_Factory(string? canonical) {
        var process = new SchemataProcess { Name = "leaf-only", CanonicalName = canonical };
        var resolved = new TargetState("task", null, false);
        var spawner = new SchemataProcessToken { CanonicalName = "processes/leaf-only/tokens/root" };
        Assert.Throws<InvalidOperationException>(() => TokenFactory<SchemataProcess, SchemataProcessToken>.NewRootToken(process, resolved));
        Assert.Throws<InvalidOperationException>(() => TokenFactory<SchemataProcess, SchemataProcessToken>.NewChildToken(process, resolved, spawner));
        Assert.Throws<InvalidOperationException>(() => TransitionFactory<SchemataProcessTransition>.New(process, spawner.CanonicalName, null, "task", TransitionKind.Move, "Start"));
    }

    [Fact]
    public void Every_Row_Factory_Uses_The_Canonical_Owner_And_Leaves_Name_To_Consumer() {
        var process = new SchemataProcess { Name = "leaf", CanonicalName = "processes/leaf" };
        var resolved = new TargetState("task", null, false);
        var root = TokenFactory<SchemataProcess, SchemataProcessToken>.NewRootToken(process, resolved);
        root.CanonicalName = "processes/leaf/tokens/root";
        var child = TokenFactory<SchemataProcess, SchemataProcessToken>.NewChildToken(process, resolved, root);
        var transition = TransitionFactory<SchemataProcessTransition>.New(process, root.CanonicalName, null, "task", TransitionKind.Move, "Start");
        Assert.Equal("processes/leaf", root.Process);
        Assert.Equal("processes/leaf", child.Process);
        Assert.Equal("processes/leaf", transition.Process);
        Assert.Equal(root.CanonicalName, child.Spawner);
        Assert.Null(root.Name);
        Assert.Null(child.Name);
        Assert.Null(transition.Name);
    }
}
