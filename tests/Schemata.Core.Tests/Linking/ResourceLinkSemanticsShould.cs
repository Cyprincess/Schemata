using System.Collections.Generic;
using System.Linq;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Xunit;

namespace Schemata.Core.Tests.Linking;

[Trait("Layer", "Unit")]
public class ResourceLinkSemanticsShould
{
    [Fact]
    public void Paging_Reads_ExplicitContract_And_PreservesScope_On_First_Previous_Last() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Book>(), []);
        var policy = new Mock<IResourceLinkPolicy>();
        policy.Setup(value => value.ReadPage("current")).Returns(new LinkPage(7, 14, "current"));
        policy.Setup(value => value.ReadPage("next")).Returns(new LinkPage(7, 21, "next"));
        policy.Setup(value => value.WritePage(It.Is<LinkPage>(page => page.PageSize == 7 && page.Token == "current"), 0)).Returns("first");
        policy.Setup(value => value.WritePage(It.Is<LinkPage>(page => page.Token == "current"), 7)).Returns("previous");
        policy.Setup(value => value.WritePage(It.Is<LinkPage>(page => page.Token == "current"), 28)).Returns("last");
        policy.Setup(value => value.EffectiveTotalSize(It.IsAny<ResourceRegistration>())).Returns(TotalSizeMode.Exact);
        var request = new Mock<ILinkRequest>();
        request.Setup(value => value.GetInput("page_token")).Returns("current");
        var result = new Mock<IEntitiesResult>();
        result.SetupGet(value => value.NextPageToken).Returns("next");
        result.SetupGet(value => value.TotalSize).Returns(30);
        var target = new ResourceCollectionTarget(typeof(Book), "libraries/one", "title=one");
        var links = new ResourceLinkSemantics(registry, policy.Object).GetLinks(
            new(target, result.Object, registry.GetResource(typeof(Book)), request.Object)).ToArray();
        Assert.Equal(new[] { "self", "next", "first", "prev", "last" }, links.Select(link => link.Relation));
        foreach (var link in links.Where(link => link.Relation != "self")) {
            var collection = Assert.IsType<ResourceCollectionTarget>(link.Target);
            Assert.Equal("libraries/one", collection.Parent);
            Assert.Equal("title=one", collection.Filter);
        }
        Assert.Equal("first", links.Single(link => link.Relation == "first").Target.Parameters!["page_token"]);
        policy.Verify(value => value.WritePage(It.Is<LinkPage>(page => page.PageSize == 7), 0), Times.Once);
    }

    [Fact]
    public void Estimated_Total_Omits_Last_Navigation() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Book>(), []);
        var policy = new Mock<IResourceLinkPolicy>();
        policy.Setup(value => value.ReadPage("next")).Returns(new LinkPage(2, 2, "next"));
        policy.Setup(value => value.EffectiveTotalSize(It.IsAny<ResourceRegistration>())).Returns(TotalSizeMode.Estimated);
        var links = new ResourceLinkSemantics(registry, policy.Object).GetLinks(new(
            new ResourceCollectionTarget(typeof(Book)), new ListResultBase<Book, Book> { NextPageToken = "next", TotalSize = 100 },
            registry.GetResource(typeof(Book)))).ToArray();
        Assert.Equal(new[] { "self", "next" }, links.Select(link => link.Relation));
    }

    [Fact]
    public void Instance_Uses_One_Canonical_Parent_And_Immediate_Child_Collection() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Library>(), []);
        registry.Add(new ResourceAttribute<Book>(), []);
        var library = registry.GetResource(typeof(Library));
        var policy = new Mock<IResourceLinkPolicy>();
        var links = new ResourceLinkSemantics(registry, policy.Object).GetLinks(new(
            new ResourceInstanceTarget(typeof(Library), "libraries/one"),
            new Library { CanonicalName = "libraries/one" }, library)).ToArray();
        Assert.Equal(new[] { "self", "canonical", "related" }, links.Select(link => link.Relation));
        var child = Assert.IsType<ResourceCollectionTarget>(links.Single(link => link.Relation == "related").Target);
        Assert.Equal(typeof(Book), child.Entity);
        Assert.Equal("libraries/one", child.Parent);
        var named = new Mock<ICanonicalName>();
        named.SetupGet(value => value.CanonicalName).Returns("libraries/one/books/two");
        var nested = new ResourceLinkSemantics(registry, policy.Object).GetLinks(new(
            new ResourceInstanceTarget(typeof(Book), "libraries/one/books/two"), named.Object,
            registry.GetResource(typeof(Book)))).ToArray();
        Assert.Equal("libraries/one", Assert.IsType<ResourceInstanceTarget>(nested.Single(link => link.Relation == "up").Target).CanonicalName);
    }

    [Fact]
    public void Custom_Method_Response_Uses_Its_Own_Registered_Canonical_Owner() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Library>(), []);
        registry.Add(new ResourceAttribute<Book>(), []);
        var result = new Mock<ICanonicalName>();
        result.SetupGet(value => value.CanonicalName).Returns("libraries/one/books/two");
        var semantics = new ResourceLinkSemantics(registry, Mock.Of<IResourceLinkPolicy>());
        var links = semantics.GetLinks(new(
            new ResourceInstanceTarget(typeof(Library), "libraries/one", "run"), result.Object,
            registry.GetResource(typeof(Library)))).ToArray();
        Assert.All(links.Where(link => link.Relation is "self" or "canonical"), link => {
            var target = Assert.IsType<ResourceInstanceTarget>(link.Target);
            Assert.Equal(typeof(Book), target.Entity);
            Assert.Equal("libraries/one/books/two", target.CanonicalName);
            Assert.Null(target.Verb);
        });
        Assert.Equal("libraries/one", Assert.IsType<ResourceInstanceTarget>(links.Single(link => link.Relation == "up").Target).CanonicalName);
    }

    [CanonicalName("libraries/{library}")]
    private sealed class Library : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("libraries/{library}/books/{book}")]
    private sealed class Book;
}
