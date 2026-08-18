using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

public class PurgeJobKeyResolverShould
{
    [Fact]
    public void ResolveType_MapsPurgeKeyToClosedGenericJob() {
        Assert.Equal(typeof(PurgeJob<TrashStudent>), Resolver().ResolveType("purge:trashStudents"));
    }

    [Fact]
    public void ResolveType_ReturnsNull_ForNonSoftDeleteCollection() {
        // Student is a registered resource but is not ISoftDelete, so purge does not apply.
        Assert.Null(Resolver().ResolveType("purge:students"));
    }

    [Fact]
    public void ResolveType_ReturnsNull_ForUnknownCollection() {
        Assert.Null(Resolver().ResolveType("purge:unknown"));
    }

    [Fact]
    public void ResolveType_ReturnsNull_ForForeignKey() {
        Assert.Null(Resolver().ResolveType("jobs:something"));
    }

    [Fact]
    public void ResolveKey_MapsClosedGenericJobToPurgeKey() {
        Assert.Equal("purge:trashStudents", Resolver().ResolveKey(typeof(PurgeJob<TrashStudent>)));
    }

    [Fact]
    public void ResolveKey_ReturnsNull_ForNonPurgeJobType() {
        Assert.Null(Resolver().ResolveKey(typeof(string)));
    }

    private static PurgeJobKeyResolver Resolver() {
        var registry = new ResourceRegistry();
        registry.Add(new(typeof(TrashStudent)), []);
        registry.Add(new(typeof(Student)), []);
        return new(new DefaultResourceTypeResolver(registry));
    }
}
