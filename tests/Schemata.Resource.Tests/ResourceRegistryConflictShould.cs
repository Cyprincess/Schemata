using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Skeleton.Entities;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceRegistryConflictShould
{
    [Fact]
    public void Registration_Rejects_Duplicate_Canonical_Pattern() {
        var registry = Registry(typeof(Student));

        var error = Assert.Throws<InvalidOperationException>(() => registry.Add(new(typeof(StudentAlias)), []));

        Assert.Contains("canonical pattern", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Student).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(StudentAlias).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Single(registry.Resources);
    }

    [Theory]
    [InlineData(typeof(RenamedPlaceholderStudent))]
    [InlineData(typeof(CaseVariantStudent))]
    public void Registration_Rejects_Pattern_That_Normalizes_To_A_Registered_One(Type conflicting) {
        var registry = Registry(typeof(Student));

        var error = Assert.Throws<InvalidOperationException>(() => registry.Add(new(conflicting), []));

        Assert.Contains("canonical pattern", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Student).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_Rejects_Duplicate_Collection_Name() {
        var registry = Registry(typeof(Student));

        var error = Assert.Throws<InvalidOperationException>(() => registry.Add(new(typeof(EnrolledStudent)), []));

        Assert.Contains("collection name 'students'", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Student).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Single(registry.Resources);
    }

    [Fact]
    public void Rejected_Registration_Does_Not_Claim_A_Pattern() {
        var registry = Registry(typeof(Student));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new(typeof(EnrolledStudent)), []));

        var error = Assert.Throws<InvalidOperationException>(() => registry.Add(new(typeof(EnrolledStudent)), []));

        Assert.Contains("collection name", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("canonical pattern", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejected_Pattern_Does_Not_Claim_Its_Collection() {
        var registry = Registry(typeof(Student));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new(typeof(CaseVariantStudent)), []));

        registry.Add(new(typeof(NestedCaseVariantStudent)), []);

        Assert.Equal(2, registry.Resources.Count);
    }

    [Fact]
    public void Registration_Rejects_Authorization_And_Flow_Token_Collections() {
        var registry = Registry(typeof(SchemataToken));

        var error = Assert.Throws<InvalidOperationException>(() => registry.Add(new(typeof(SchemataProcessToken)), []));

        Assert.Contains("collection name 'tokens'", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SchemataToken).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("tokens/{token}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_Allows_Repeated_Registration_Of_One_Entity() {
        var registry = Registry(typeof(Student));

        registry.Add(new(typeof(Student)) { Endpoints = ["Http"] }, []);

        Assert.Single(registry.Resources);
    }

    [Fact]
    public async Task Typed_Reference_Resolves_To_The_Unique_Target() {
        var registry = Registry(typeof(Book), typeof(Student));
        var resolver = new DefaultResourceTypeResolver(registry);
        var (advisor, context, repository, provider) = ReferenceHost(resolver);
        using var _ = provider;
        var entity = new ReferencingEntity { Book = "publishers/acme/books/les-miserables" };

        var result = await advisor.AdviseAsync(context, repository, entity, default, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Same(typeof(Book), resolver.Resolve(entity.Book));
    }

    [Fact]
    public async Task Polymorphic_Reference_Resolves_To_The_Unique_Target() {
        var registry = Registry(typeof(Book), typeof(Student));
        var resolver = new DefaultResourceTypeResolver(registry);
        var (advisor, context, repository, provider) = ReferenceHost(resolver);
        using var _ = provider;
        var entity = new ReferencingEntity { Subject = "publishers/acme/books/les-miserables" };

        var result = await advisor.AdviseAsync(context, repository, entity, default, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Same(typeof(Book), resolver.Resolve(entity.Subject));
        Assert.Same(typeof(Student), resolver.Resolve("students/alice"));
    }

    private static ResourceRegistry Registry(params Type[] entities) {
        var registry = new ResourceRegistry();
        foreach (var entity in entities) {
            registry.Add(new(entity), []);
        }

        return registry;
    }

    private static (AdviceValidateResourceReferences<ReferencingEntity> Advisor, AdviceContext Context,
        IRepository<ReferencingEntity> Repository, ServiceProvider Provider) ReferenceHost(IResourceTypeResolver resolver) {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        var provider = services.BuildServiceProvider();
        return (new(), new AdviceContext(provider), new Mock<IRepository<ReferencingEntity>>().Object, provider);
    }

    [CanonicalName("students/{student}")]
    private sealed class StudentAlias
    {
        public string? Name { get; set; }
    }

    [CanonicalName("students/{studentId}")]
    private sealed class RenamedPlaceholderStudent
    {
        public string? Name { get; set; }
    }

    [CanonicalName("Students/{student}")]
    private sealed class CaseVariantStudent
    {
        public string? Name { get; set; }
    }

    [CanonicalName("schools/{school}/students/{student}")]
    private sealed class EnrolledStudent
    {
        public string? School { get; set; }
        public string? Name   { get; set; }
    }

    [CanonicalName("schools/{school}/Students/{student}")]
    private sealed class NestedCaseVariantStudent
    {
        public string? School { get; set; }
        public string? Name   { get; set; }
    }

    public sealed class ReferencingEntity
    {
        [ResourceReference(typeof(Book))]
        public string? Book { get; set; }

        [ResourceReference]
        public string? Subject { get; set; }
    }
}
