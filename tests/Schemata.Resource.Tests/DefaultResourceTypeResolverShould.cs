using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

public class DefaultResourceTypeResolverShould
{
    [Fact]
    public void ResolveCollection_MapsCollectionToEntity() {
        var resolver = Resolver();

        Assert.Equal(typeof(TrashStudent), resolver.ResolveCollection("trashStudents"));
        Assert.Equal(typeof(Student), resolver.ResolveCollection("students"));
    }

    [Fact]
    public void ResolveCollection_ReturnsNull_ForUnknown() {
        Assert.Null(Resolver().ResolveCollection("unknown"));
    }

    [Fact]
    public void Resolve_MatchesFullCanonicalName() {
        var resolver = Resolver();

        Assert.Equal(typeof(TrashStudent), resolver.Resolve("trashStudents/abc"));
        Assert.Equal(typeof(Student), resolver.Resolve("students/xyz"));
    }

    [Fact]
    public void Resolve_FallsBackToBareCollection() {
        Assert.Equal(typeof(TrashStudent), Resolver().Resolve("trashStudents"));
    }

    [Fact]
    public void Resolve_ReturnsNull_ForEmptyOrUnmatched() {
        var resolver = Resolver();

        Assert.Null(resolver.Resolve(""));
        Assert.Null(resolver.Resolve("nope/abc"));
    }

    private static IResourceTypeResolver Resolver() {
        var options = new SchemataResourceOptions();
        options.Resources[typeof(TrashStudent).TypeHandle] = new ResourceAttribute(typeof(TrashStudent));
        options.Resources[typeof(Student).TypeHandle]      = new ResourceAttribute(typeof(Student));
        return new DefaultResourceTypeResolver(Options.Create(options));
    }
}
