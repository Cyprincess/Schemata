using System;
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

    [Fact]
    public void Resolve_MatchesNestedCanonicalName() {
        var resolver = Resolver(typeof(Book));

        Assert.Equal(typeof(Book), resolver.Resolve("publishers/acme/books/les-miserables"));
    }

    [Fact]
    public void Resolve_RejectsCanonicalName_WithWrongSegmentCount() {
        var resolver = Resolver(typeof(Book));

        Assert.Null(resolver.Resolve("publishers/acme/books"));
        Assert.Null(resolver.Resolve("publishers/acme/books/x/y"));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_ForLiteralSegments() {
        var resolver = Resolver();

        Assert.Equal(typeof(Student), resolver.Resolve("STUDENTS/xyz"));
        Assert.Equal(typeof(Student), resolver.Resolve("Students/xyz"));
    }

    [Fact]
    public void Resolve_PrefersLiteralBranch_OverPlaceholder() {
        var resolver = Resolver(typeof(TenantUser), typeof(GlobalSetting));

        Assert.Equal(typeof(GlobalSetting), resolver.Resolve("tenants/global/settings/timezone"));
    }

    [Fact]
    public void Resolve_BacktracksToPlaceholder_WhenLiteralPathDeadEnds() {
        var resolver = Resolver(typeof(TenantUser), typeof(GlobalSetting));

        // "global" matches the literal branch first, but "users" has no child there,
        // so the walker must backtrack to the placeholder branch under "tenants".
        Assert.Equal(typeof(TenantUser), resolver.Resolve("tenants/global/users/alice"));
    }

    [Fact]
    public void Resolve_HandlesAdjacentSlashes_AsUnmatched() {
        var resolver = Resolver();

        Assert.Null(resolver.Resolve("students//xyz"));
    }

    private static IResourceTypeResolver Resolver(params Type[] additional) {
        var options = new SchemataResourceOptions();
        options.Resources[typeof(TrashStudent).TypeHandle] = new(typeof(TrashStudent));
        options.Resources[typeof(Student).TypeHandle]      = new(typeof(Student));
        foreach (var type in additional) {
            options.Resources[type.TypeHandle] = new(type);
        }

        return new DefaultResourceTypeResolver(Options.Create(options));
    }
}
