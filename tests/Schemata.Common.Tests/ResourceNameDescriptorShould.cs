using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Common.Tests;

public class ResourceNameDescriptorShould
{
    [Fact]
    public void Resolve_MapLeafToName_ForCamelCaseCollection() {
        var descriptor = ResourceNameDescriptor.ForType<SalesOrder>();

        Assert.Equal("SalesOrder", descriptor.Singular);
        Assert.Equal("SalesOrders", descriptor.Plural);
        Assert.Equal("salesOrders/1", descriptor.Resolve(new SalesOrder { Name = "1" }));
    }

    [Fact]
    public void Resolve_MapLeafToName_ForHyphenatedCollection() {
        var descriptor = ResourceNameDescriptor.ForType<EventSubscription>();

        Assert.Equal("EventSubscription", descriptor.Singular);
        Assert.Equal("EventSubscriptions", descriptor.Plural);
        Assert.Equal("event-subscriptions/1", descriptor.Resolve(new EventSubscription { Name = "1" }));
    }

    [Fact]
    public void Resolve_MapLeafToName_WhenTheLeafPlaceholderIsNotTheSingular() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        Assert.Equal("books/1", descriptor.Resolve(new Book { Name = "1" }));
    }

    [Fact]
    public void Resolve_MapLeafToName_WhenTheLeafPlaceholderIsNamedParent() {
        var descriptor = ResourceNameDescriptor.ForType<Parent>();

        Assert.Equal("parents/1", descriptor.Resolve(new Parent { Name = "1" }));
    }

    [Fact]
    public void ParentOf_StripsCollectionAndLeaf() {
        var descriptor = ResourceNameDescriptor.ForType<Widget>();

        Assert.Equal("tenants/acme", descriptor.ParentOf("tenants/acme/widgets/w1"));
        Assert.Equal("organizations/o/projects/p", descriptor.ParentOf("organizations/o/projects/p/datasets/d"));
        Assert.Null(descriptor.ParentOf("tenants/acme"));
        Assert.Null(descriptor.ParentOf("not/a/canonical"));
        Assert.Null(descriptor.ParentOf(null));
    }

    [CanonicalName("salesOrders/{salesOrder}")]
    private sealed class SalesOrder : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("tenants/{tenant}/widgets/{widget}")]
    private sealed class Widget : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("event-subscriptions/{event_subscription}")]
    private sealed class EventSubscription : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("books/{bookId}")]
    private sealed class Book : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("parents/{parent}")]
    private sealed class Parent : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
