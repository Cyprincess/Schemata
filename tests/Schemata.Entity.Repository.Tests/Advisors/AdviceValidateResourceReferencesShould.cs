using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceValidateResourceReferencesShould
{
    [Fact]
    public async Task TypedReference_DoesNotThrow_WhenTargetMatches() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/x")).Returns(typeof(Book));

        var (advisor, ctx, repo) = Build(resolver.Object);
        var entity = new ReferencingEntity { BookCanonicalName = "books/x" };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task TypedReference_ThrowsNotFound_WhenTargetMismatch() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/x")).Returns(typeof(string));

        var (advisor, ctx, repo) = Build(resolver.Object);
        var entity = new ReferencingEntity { BookCanonicalName = "books/x" };

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None));

        Assert.NotNull(exception.Details);
        var resource = Assert.Single(exception.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/x", resource.ResourceName);
    }

    [Fact]
    public async Task TypedReference_ThrowsNotFound_WhenResolverReturnsNull() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/missing")).Returns((Type?)null);

        var (advisor, ctx, repo) = Build(resolver.Object);
        var entity = new ReferencingEntity { BookCanonicalName = "books/missing" };

        await Assert.ThrowsAsync<NotFoundException>(
            () => advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None));
    }

    [Fact]
    public async Task PolymorphicReference_DoesNotThrow_WhenResolverHits() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("users/alice")).Returns(typeof(string));

        var (advisor, ctx, repo) = Build(resolver.Object);
        var entity = new ReferencingEntity { Subject = "users/alice" };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task PolymorphicReference_ThrowsValidation_WhenResolverMisses() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("unknown/x")).Returns((Type?)null);

        var (advisor, ctx, repo) = Build(resolver.Object);
        var entity = new ReferencingEntity { Subject = "unknown/x" };

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None));

        Assert.NotNull(exception.Details);
        var badRequest = Assert.Single(exception.Details!.OfType<BadRequestDetail>());
        var violation  = Assert.Single(badRequest.FieldViolations!);
        Assert.Equal(nameof(ReferencingEntity.Subject), violation.Field);
        Assert.Equal("INVALID_REFERENCE", violation.Reason);
    }

    [Fact]
    public async Task NullValue_SkipsValidation() {
        var resolver = new Mock<IResourceTypeResolver>(MockBehavior.Strict);

        var (advisor, ctx, repo) = Build(resolver.Object);
        var entity = new ReferencingEntity { BookCanonicalName = null, Subject = null };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReferencedEntity_MissingResolver_Continues() {
        var advisor    = new AdviceValidateResourceReferences<ReferencingEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<ReferencingEntity>>().Object;
        var entity     = new ReferencingEntity { BookCanonicalName = "books/x" };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task EntityWithNoResourceReferenceAttributes_Continues() {
        var advisor = new AdviceValidateResourceReferences<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;

        var result = await advisor.AdviseAsync(ctx, repository, new(), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    private static (AdviceValidateResourceReferences<ReferencingEntity> Advisor, AdviceContext Ctx,
        IRepository<ReferencingEntity> Repo) Build(IResourceTypeResolver resolver) {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var advisor    = new AdviceValidateResourceReferences<ReferencingEntity>();
        var repository = new Mock<IRepository<ReferencingEntity>>().Object;
        return (advisor, ctx, repository);
    }

    #region Nested type: Book

    public sealed class Book : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion

    #region Nested type: ReferencingEntity

    public sealed class ReferencingEntity
    {
        [ResourceReference(typeof(Book))]
        public string? BookCanonicalName { get; set; }

        [ResourceReference]
        public string? Subject { get; set; }

        public string? UntaggedFk { get; set; }
    }

    #endregion

    #region Nested type: PlainEntity

    public sealed class PlainEntity
    {
        public long Id { get; set; }
    }

    #endregion
}
