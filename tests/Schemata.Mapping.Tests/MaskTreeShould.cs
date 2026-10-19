using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Mapping.Tests;

public class MaskTreeShould
{
    [Fact]
    public void WholeFieldSelection_DominatesDescendantPaths_InAnyOrder() {
        var whole       = MaskTree.FromClr(typeof(Order), ["Address"], false);
        var parentFirst = MaskTree.FromClr(typeof(Order), ["Address", "Address.City"], false);
        var childFirst  = MaskTree.FromClr(typeof(Order), ["Address.City", "Address"], false);

        Assert.Equal(["Address"], whole.LeafPaths().ToArray());
        Assert.Equal(["Address"], parentFirst.LeafPaths().ToArray());
        Assert.Equal(["Address"], childFirst.LeafPaths().ToArray());
    }

    [Fact]
    public void WholeFieldSelection_MasksEntireSubtree_ForWalkUnmasked() {
        var tree  = MaskTree.FromClr(typeof(Order), ["Address", "Address.City"], false);
        var order = new Order { Id = "order-1", Note = "note", Address = new() { City = "city", Street = "street" } };

        var unmasked = new List<string>();
        MaskWalker.WalkUnmasked(order, tree.Children, false, (_, _, property) => unmasked.Add(property.Name));

        Assert.Equal(["Id", "Note"], unmasked);
    }

    [Fact]
    public void DescendantOnlySelection_LeavesSiblingAndInteriorFieldsUnmasked() {
        var tree  = MaskTree.FromClr(typeof(Order), ["Address.City"], false);
        var order = new Order { Id = "order-1", Note = "note", Address = new() { City = "city", Street = "street" } };

        var unmasked = new List<string>();
        MaskWalker.WalkUnmasked(order, tree.Children, false, (_, _, property) => unmasked.Add(property.Name));

        Assert.Equal(3, unmasked.Count);
        Assert.Contains("Id", unmasked);
        Assert.Contains("Note", unmasked);
        Assert.Contains("Street", unmasked);
        Assert.DoesNotContain("City", unmasked);
        Assert.DoesNotContain("Address", unmasked);
    }

    [Fact]
    public void DisjointLeaves_AreRetained() {
        var tree  = MaskTree.FromClr(typeof(Order), ["Address.City", "Note"], false);
        var paths = tree.LeafPaths().ToArray();

        Assert.Equal(2, paths.Length);
        Assert.Contains("Address.City", paths);
        Assert.Contains("Note", paths);
    }

    [Fact]
    public void InvalidPaths_StillThrow_UnderTerminalParents() {
        Assert.Throws<ArgumentException>(() => MaskTree.FromClr(typeof(Order), ["Nowhere"], false));
        Assert.Throws<ArgumentException>(() => MaskTree.FromClr(typeof(Order), ["Address", "Address.Nowhere"], false));
        Assert.Throws<ArgumentException>(() => MaskTree.FromClr(typeof(Order), ["Note.Street"], false));
    }

    private sealed class Order
    {
        public string? Id { get; set; }

        public Address? Address { get; set; }

        public string? Note { get; set; }
    }

    private sealed class Address
    {
        public string? City { get; set; }

        public string? Street { get; set; }
    }
}
