using Microsoft.AspNetCore.Routing;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Resource.Http.Internal;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

public class ResourceIdentifiersShould
{
    [Fact]
    public void BuildFullName_SimpleResource_ReturnsCollectionAndName() {
        var descriptor = ResourceNameDescriptor.ForType<SimpleThing>();

        var name = ResourceIdentifiers.BuildFullName(descriptor, new(), "one");

        Assert.Equal("simplethings/one", name);
    }

    [Fact]
    public void BuildFullName_MultiSegmentParent_ReturnsParentCollectionAndName() {
        var descriptor = ResourceNameDescriptor.ForType<ChildThing>();
        var route = new RouteValueDictionary {
            ["org"]     = "acme",
            ["project"] = "alpha",
        };

        var name = ResourceIdentifiers.BuildFullName(descriptor, route, "item1");

        Assert.Equal("orgs/acme/projects/alpha/children/item1", name);
    }

    [Fact]
    public void BuildFullName_WildcardParent_ReturnsWildcardInParentPath() {
        var descriptor = ResourceNameDescriptor.ForType<ChildThing>();
        var route = new RouteValueDictionary {
            ["org"]     = "-",
            ["project"] = "alpha",
        };

        var name = ResourceIdentifiers.BuildFullName(descriptor, route, "item1");

        Assert.Equal("orgs/-/projects/alpha/children/item1", name);
    }

    private sealed class SimpleThing : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    [CanonicalName("orgs/{org}/projects/{project}/children/{child}")]
    private sealed class ChildThing : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
