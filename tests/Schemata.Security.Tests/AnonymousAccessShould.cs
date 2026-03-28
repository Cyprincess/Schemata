using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Tests.Fixtures;
using Xunit;

namespace Schemata.Security.Tests;

public class AnonymousAccessShould
{
    [Fact]
    public void IsAnonymous_NoAttribute_ReturnsFalse() {
        Assert.False(AnonymousAccess.IsAnonymous<Product>(nameof(Operations.Create)));
    }

    [Fact]
    public void IsAnonymous_AttributeWithMatchingOperation_ReturnsTrue() {
        // PublicProduct has [Anonymous(Operations.Create, Operations.List)]
        Assert.True(AnonymousAccess.IsAnonymous<PublicProduct>(nameof(Operations.Create)));
        Assert.True(AnonymousAccess.IsAnonymous<PublicProduct>(nameof(Operations.List)));
    }

    [Fact]
    public void IsAnonymous_AttributeWithNonMatchingOperation_ReturnsFalse() {
        Assert.False(AnonymousAccess.IsAnonymous<PublicProduct>(nameof(Operations.Delete)));
    }

    [Fact]
    public void IsAnonymous_AttributeWithNoOperations_AllOperationsAnonymous() {
        // FullyPublicProduct has [Anonymous] (no specific operations)
        Assert.True(AnonymousAccess.IsAnonymous<FullyPublicProduct>(nameof(Operations.Create)));
        Assert.True(AnonymousAccess.IsAnonymous<FullyPublicProduct>(nameof(Operations.Delete)));
        Assert.True(AnonymousAccess.IsAnonymous<FullyPublicProduct>(nameof(Operations.Get)));
    }

    [Fact]
    public void IsAnonymous_StringConstructor_MatchingEvent_ReturnsTrue() {
        // EventWorkflow has [Anonymous("Approve", "Reject")]
        Assert.True(AnonymousAccess.IsAnonymous<EventWorkflow>("Approve"));
        Assert.True(AnonymousAccess.IsAnonymous<EventWorkflow>("Reject"));
    }

    [Fact]
    public void IsAnonymous_StringConstructor_NonMatchingEvent_ReturnsFalse() {
        Assert.False(AnonymousAccess.IsAnonymous<EventWorkflow>("Cancel"));
    }

    [Fact]
    public void IsAnonymous_StringConstructor_CaseInsensitive_ReturnsTrue() {
        Assert.True(AnonymousAccess.IsAnonymous<EventWorkflow>("approve"));
    }
}
