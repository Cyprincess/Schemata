using System.ComponentModel;
using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Common.Tests;

public class ResourceNameDescriptorShould
{
    [Fact]
    public void Resolve_MapLeafToName_ForDisplayNameContainingSpaces() {
        var descriptor = ResourceNameDescriptor.ForType<SalesOrder>();

        var resolved = descriptor.Resolve(new SalesOrder { Name = "1" });

        Assert.Equal("salesOrders/1", resolved);
    }

    [Fact]
    public void Resolve_MapLeafToName_ForPascalDisplayName() {
        var descriptor = ResourceNameDescriptor.ForType<Invoice>();

        var resolved = descriptor.Resolve(new Invoice { Name = "1" });

        Assert.Equal("invoices/1", resolved);
    }

    [Fact]
    public void Resolve_MapLeafToName_ForTypeNameWithoutDisplayName() {
        var descriptor = ResourceNameDescriptor.ForType<Shipment>();

        var resolved = descriptor.Resolve(new Shipment { Name = "1" });

        Assert.Equal("shipments/1", resolved);
    }

    [DisplayName("Sales Order")]
    [CanonicalName("salesOrders/{salesOrder}")]
    private sealed class SalesOrder : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [DisplayName("Invoice")]
    [CanonicalName("invoices/{invoice}")]
    private sealed class Invoice : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("shipments/{shipment}")]
    private sealed class Shipment : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }
}
