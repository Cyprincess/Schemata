using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Common.Tests;

[Trait("Category", "Unit")]
public sealed class CanonicalParentReferenceShould
{
    [Fact]
    public void Resolve_Bind_And_Filter_The_Declared_Canonical_Parent() {
        var descriptor = ResourceNameDescriptor.ForType<ReferencedItem>();
        var item = new ReferencedItem { Name = "item", Parent = "parents/one" };
        Assert.Equal("parents/one/items/item", descriptor.Resolve(item));
        var predicate = descriptor.BuildParentPredicate<ReferencedItem>(new() { ["parent"] = "one" })!.Compile();
        Assert.True(predicate(item));
        Assert.False(predicate(new() { Name = "item", Parent = "parents/two" }));
        Assert.False(predicate(new() { Name = "item", Parent = "one" }));
        descriptor.SetParentFromRouteValues(item, new Dictionary<string, object?> { ["parent"] = "two" });
        Assert.Equal("parents/two", item.Parent);
        Assert.Equal("parents/two/items/item", descriptor.Resolve(item));
    }

    [Fact]
    public void Ordinary_Leaf_Parent_And_Unrelated_Reference_Keep_Their_Own_Semantics() {
        var descriptor = ResourceNameDescriptor.ForType<LeafItem>();
        var item = new LeafItem { Name = "item", Parent = "one", Related = "parents/other" };
        Assert.Equal("parents/one/items/item", descriptor.Resolve(item));
        Assert.True(descriptor.BuildParentPredicate<LeafItem>(new() { ["parent"] = "one" })!.Compile()(item));
        descriptor.SetParentFromRouteValues(item, new Dictionary<string, object?> { ["parent"] = "two" });
        Assert.Equal("two", item.Parent);
        Assert.Equal("parents/other", item.Related);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("one")]
    [InlineData("parents/-")]
    [InlineData("other/one")]
    [InlineData("parents/one/extra")]
    public void Reject_Noncanonical_Reference_Without_Leaf_Fallback(string? parent) {
        Assert.Throws<ValidationException>(() => ResourceNameDescriptor.ForType<ReferencedItem>().Resolve(new ReferencedItem { Name = "item", Parent = parent }));
    }

    [CanonicalName("parents/{parent}")]
    private sealed class ParentResource : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("parents/{parent}/items/{item}")]
    private sealed class ReferencedItem : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        [ResourceReference(typeof(ParentResource))]
        public string? Parent { get; set; }
    }

    [CanonicalName("parents/{parent}/items/{item}")]
    private sealed class LeafItem : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public string? Parent { get; set; }
        [ResourceReference(typeof(ParentResource))]
        public string? Related { get; set; }
    }
}
