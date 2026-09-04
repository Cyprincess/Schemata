using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Common.Tests;

public class ResourceNameStructuralIdentityShould
{
    [Fact]
    public void AgreeOnIdentity_WhenTheClrNameDiffersFromTheResourceName() {
        var descriptor = ResourceNameDescriptor.ForType<InternalExecution>();

        Assert.Equal("Operation", descriptor.Singular);
        Assert.Equal("Operations", descriptor.Plural);
        Assert.Equal("operations", descriptor.Collection);
        Assert.Equal("operations", descriptor.CollectionPath);
        Assert.Equal("operations/x1", descriptor.Resolve(new InternalExecution { Name = "x1" }));

        var parsed = descriptor.ParseCanonicalName("operations/x1");
        Assert.NotNull(parsed);
        Assert.Equal("x1", parsed.Value.LeafName);
        Assert.Empty(parsed.Value.ParentValues);
    }

    [Fact]
    public void ScopeAMultiLevelParentToItsExactBranch() {
        var container = new ResourceRequestContainer<ScopedChild>();
        ResourceIdentifiers.ApplyParent(container, "orgs/o1/projects/p1");

        Assert.Equal(["leaf", "other"], Names(container));
    }

    [Fact]
    public void ScopeAFullCanonicalNameToASingleRow() {
        var container = new ResourceRequestContainer<ScopedChild>();
        ResourceIdentifiers.Apply(container, "orgs/o1/projects/p1/children/leaf");

        var row = Assert.Single(container.Query(Rows().AsQueryable()));
        Assert.Equal("o1", row.Org);
        Assert.Equal("p1", row.Project);
        Assert.Equal("leaf", row.Name);
    }

    [Fact]
    public void KeepTheInnerScope_WhenAnOuterParentIsWildcarded() {
        var container = new ResourceRequestContainer<ScopedChild>();
        ResourceIdentifiers.ApplyParent(container, "orgs/-/projects/p1");

        var scoped = container.Query(Rows().AsQueryable()).ToList();
        Assert.Equal(3, scoped.Count);
        Assert.All(scoped, row => Assert.Equal("p1", row.Project));
        Assert.Equal(["o1", "o2"], scoped.Select(row => row.Org).Distinct().OrderBy(org => org, StringComparer.Ordinal));
    }

    [Fact]
    public void LeaveThePredicateOpen_WhenEveryParentIsWildcarded() {
        var descriptor = ResourceNameDescriptor.ForType<ScopedChild>();

        var predicate = descriptor.BuildParentPredicate<ScopedChild>(new() { ["org"] = "-", ["project"] = "-" });

        Assert.Null(predicate);
    }

    [Fact]
    public void FailLoudly_WhenAParentPlaceholderHasNoProperty() {
        var descriptor = ResourceNameDescriptor.ForType<UnscopedItem>();

        Assert.Throws<MissingFieldException>(
            () => descriptor.BuildParentPredicate<UnscopedItem>(new() { ["tenant"] = "t1" }));
    }

    private static List<string?> Names(ResourceRequestContainer<ScopedChild> container) {
        return container.Query(Rows().AsQueryable())
                        .Select(row => row.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList();
    }

    private static ScopedChild[] Rows() {
        return [
            new() { Org = "o1", Project = "p1", Name = "leaf" },
            new() { Org = "o1", Project = "p1", Name = "other" },
            new() { Org = "o1", Project = "p2", Name = "leaf" },
            new() { Org = "o2", Project = "p1", Name = "leaf" },
        ];
    }

    [CanonicalName("operations/{operation}")]
    private sealed class InternalExecution : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [ReadAcross]
    [CanonicalName("orgs/{org}/projects/{project}/children/{child}")]
    private sealed class ScopedChild : ICanonicalName
    {
        public string? Org           { get; set; }
        public string? Project       { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("tenants/{tenant}/items/{item}")]
    private sealed class UnscopedItem : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
