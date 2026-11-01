using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
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
    public void Scope_A_Multi_Level_Parent_To_Its_Exact_Branch() {
        var container = new ResourceRequestContainer<ScopedChild>();
        ResourceIdentifiers.ApplyParent(container, "orgs/o1/projects/p1");

        Assert.Equal(["leaf", "other"], Names(container));
    }

    [Fact]
    public void Scope_A_Full_Canonical_Name_To_A_Single_Row() {
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

    [Fact]
    public void Reject_Cross_Parent_Wildcard_In_The_Name_Without_ReadAcross() {
        var container = new ResourceRequestContainer<UnscopedItem>();

        var error = Assert.Throws<ValidationException>(
            () => ResourceIdentifiers.Apply(container, "tenants/-/items/leaf"));

        var violation = Assert.Single(error.Details!.OfType<BadRequestDetail>());
        Assert.Equal(SchemataResources.CROSS_PARENT_UNSUPPORTED, Assert.Single(violation.FieldViolations!).Reason);
    }

    [Fact]
    public void Read_Across_The_Wildcard_Parent_When_The_Name_Is_Applied_With_ReadAcross() {
        var container = new ResourceRequestContainer<ScopedChild>();
        ResourceIdentifiers.Apply(container, "orgs/-/projects/p1/children/leaf");

        var scoped = container.Query(Rows().AsQueryable()).ToList();
        Assert.All(scoped, row => {
            Assert.Equal("p1", row.Project);
            Assert.Equal("leaf", row.Name);
        });
        Assert.Equal(["o1", "o2"], scoped.Select(row => row.Org).Distinct().OrderBy(org => org, StringComparer.Ordinal));
    }

    [Fact]
    public void Reject_Empty_Identity_Segments_With_The_Existing_Identity_Errors() {
        var nameError = Assert.Throws<ValidationException>(
            () => ResourceIdentifiers.Apply(new ResourceRequestContainer<UnscopedItem>(), "tenants//items/leaf"));
        Assert.Equal(SchemataResources.INVALID_NAME, Assert.Single(Assert.Single(nameError.Details!.OfType<BadRequestDetail>()).FieldViolations!).Reason);

        var whitespaceError = Assert.Throws<ValidationException>(
            () => ResourceIdentifiers.Apply(new ResourceRequestContainer<UnscopedItem>(), "tenants/t1/items/ "));
        Assert.Equal(SchemataResources.INVALID_NAME, Assert.Single(Assert.Single(whitespaceError.Details!.OfType<BadRequestDetail>()).FieldViolations!).Reason);

        var parentError = Assert.Throws<ValidationException>(
            () => ResourceIdentifiers.ApplyParent(new ResourceRequestContainer<UnscopedItem>(), "tenants/ "));
        Assert.Equal(SchemataResources.INVALID_PARENT, Assert.Single(Assert.Single(parentError.Details!.OfType<BadRequestDetail>()).FieldViolations!).Reason);
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
