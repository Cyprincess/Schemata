using System;
using System.Collections.Generic;
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
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Owner.Tests.Advisors;

public class AdviceValidateResourceReferenceExistenceShould
{
    [Fact]
    public async Task TypedReference_ThrowsNotFound_WhenRowIsMissing() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/missing")).Returns(typeof(Book));

        var (advisor, ctx, _, _) = Build(resolver.Object, books: [], users: []);
        var entity = new ReferencingEntity { BookCanonicalName = "books/missing" };

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => advisor.AdviseAsync(ctx, new Mock<IRepository<ReferencingEntity>>().Object, entity, CancellationToken.None));

        Assert.NotNull(exception.Details);
        var resource = Assert.Single(exception.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/missing", resource.ResourceName);
    }

    [Fact]
    public async Task TypedReference_Continues_WhenRowExists() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/x")).Returns(typeof(Book));

        var (advisor, ctx, _, _) = Build(resolver.Object, books: [new() { CanonicalName = "books/x" }], users: []);
        var entity = new ReferencingEntity { BookCanonicalName = "books/x" };

        var result = await advisor.AdviseAsync(ctx, new Mock<IRepository<ReferencingEntity>>().Object, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task PolymorphicReference_ThrowsNotFound_WhenRowIsMissing() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("users/missing")).Returns(typeof(User));

        var (advisor, ctx, _, _) = Build(resolver.Object, books: [], users: []);
        var entity = new ReferencingEntity { Subject = "users/missing" };

        await Assert.ThrowsAsync<NotFoundException>(
            () => advisor.AdviseAsync(ctx, new Mock<IRepository<ReferencingEntity>>().Object, entity, CancellationToken.None));
    }

    [Fact]
    public async Task PolymorphicReference_Continues_WhenRowExists() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("users/alice")).Returns(typeof(User));

        var (advisor, ctx, _, _) = Build(resolver.Object, books: [], users: [new() { CanonicalName = "users/alice" }]);
        var entity = new ReferencingEntity { Subject = "users/alice" };

        var result = await advisor.AdviseAsync(ctx, new Mock<IRepository<ReferencingEntity>>().Object, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ExistenceQuery_SuppressesOwnerFilter() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/x")).Returns(typeof(Book));

        var (advisor, ctx, bookRepository, bookContext) = Build(
            resolver.Object,
            books: [new() { CanonicalName = "books/x" }],
            users: []);

        var suppressionObserved = false;
        bookRepository.Setup(r => r.AnyAsync(
                It.IsAny<Func<IQueryable<Book>, IQueryable<Book>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<Book>, IQueryable<Book>> _, CancellationToken _) => {
                suppressionObserved = bookContext.Has<QueryOwnerSuppressed>();
                return new ValueTask<bool>(true);
            });

        var entity = new ReferencingEntity { BookCanonicalName = "books/x" };

        await advisor.AdviseAsync(ctx, new Mock<IRepository<ReferencingEntity>>().Object, entity, CancellationToken.None);

        Assert.True(suppressionObserved);
        Assert.False(bookContext.Has<QueryOwnerSuppressed>());
    }

    [Fact]
    public async Task TypeOnlyReference_SkipsExistenceCheck() {
        var resolver = new Mock<IResourceTypeResolver>(MockBehavior.Strict);

        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        var bookRepository = new Mock<IRepository<Book>>(MockBehavior.Strict);
        services.AddSingleton(bookRepository.Object);

        var advisor    = new AdviceValidateResourceReferenceExistence<ReferencingEntity>();
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var repository = new Mock<IRepository<ReferencingEntity>>().Object;
        var entity     = new ReferencingEntity { TypeOnlyBook = "books/x" };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        bookRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NullValue_SkipsExistenceCheck() {
        var resolver = new Mock<IResourceTypeResolver>(MockBehavior.Strict);

        var advisor    = new AdviceValidateResourceReferenceExistence<ReferencingEntity>();
        var services   = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var repository = new Mock<IRepository<ReferencingEntity>>().Object;
        var entity     = new ReferencingEntity { BookCanonicalName = null, Subject = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoResolverRegistered_ThrowsInvalidOperationNamingEntityPropertyAndService() {
        var advisor    = new AdviceValidateResourceReferenceExistence<ReferencingEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<ReferencingEntity>>().Object;
        var entity     = new ReferencingEntity { BookCanonicalName = "books/x" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None));

        Assert.Contains(nameof(ReferencingEntity), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ReferencingEntity.BookCanonicalName), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IResourceTypeResolver), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTargetRepository_ThrowsInvalidOperation() {
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(r => r.Resolve("books/x")).Returns(typeof(Book));

        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);

        var advisor    = new AdviceValidateResourceReferenceExistence<ReferencingEntity>();
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var repository = new Mock<IRepository<ReferencingEntity>>().Object;
        var entity     = new ReferencingEntity { BookCanonicalName = "books/x" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None));
    }

    [Fact]
    public async Task TypedReference_TargetWithoutCanonicalName_ThrowsActionableInvalidOperation() {
        var advisor    = new AdviceValidateResourceReferenceExistence<BadReferenceEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<BadReferenceEntity>>().Object;
        var entity     = new BadReferenceEntity { Ref = "strings/x" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None));

        Assert.Contains(nameof(BadReferenceEntity.Ref), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ICanonicalName), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntityWithoutExistenceFlags_Continues() {
        var advisor    = new AdviceValidateResourceReferenceExistence<PlainReferenceEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainReferenceEntity>>(MockBehavior.Strict).Object;

        var result = await advisor.AdviseAsync(ctx, repository, new() { BookCanonicalName = "books/x" }, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    private static (AdviceValidateResourceReferenceExistence<ReferencingEntity> Advisor, AdviceContext Context,
        Mock<IRepository<Book>> BookRepository, AdviceContext BookContext) Build(
        IResourceTypeResolver resolver,
        IReadOnlyList<Book>   books,
        IReadOnlyList<User>   users) {
        var bookContext    = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var bookRepository = QueryableRepository(books, bookContext);
        var userRepository = QueryableRepository(users, new AdviceContext(new ServiceCollection().BuildServiceProvider()));

        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton(bookRepository.Object);
        services.AddSingleton(userRepository.Object);

        return (
            new AdviceValidateResourceReferenceExistence<ReferencingEntity>(),
            new AdviceContext(services.BuildServiceProvider()),
            bookRepository,
            bookContext);
    }

    private static Mock<IRepository<T>> QueryableRepository<T>(IReadOnlyList<T> rows, AdviceContext context)
        where T : class {
        var repository = new Mock<IRepository<T>>();
        repository.SetupGet(r => r.AdviceContext).Returns(context);
        repository.Setup(r => r.AnyAsync(
                It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) =>
                new ValueTask<bool>(predicate(rows.AsQueryable()).Any()));
        return repository;
    }

    #region Nested type: Book

    public sealed class Book : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion

    #region Nested type: User

    public sealed class User : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion

    #region Nested type: ReferencingEntity

    public sealed class ReferencingEntity
    {
        [ResourceReference(typeof(Book), ValidateExistence = true)]
        public string? BookCanonicalName { get; set; }

        [ResourceReference(ValidateExistence = true)]
        public string? Subject { get; set; }

        [ResourceReference(typeof(Book))]
        public string? TypeOnlyBook { get; set; }
    }

    #endregion

    #region Nested type: NotCanonical

    public sealed class NotCanonical
    {
        public string? Id { get; set; }
    }

    #endregion

    #region Nested type: BadReferenceEntity

    public sealed class BadReferenceEntity
    {
        [ResourceReference(typeof(NotCanonical), ValidateExistence = true)]
        public string? Ref { get; set; }
    }

    #endregion

    #region Nested type: PlainReferenceEntity

    public sealed class PlainReferenceEntity
    {
        [ResourceReference(typeof(Book))]
        public string? BookCanonicalName { get; set; }
    }

    #endregion
}
