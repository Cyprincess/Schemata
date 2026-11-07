using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using Schemata.Core.Building;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public class StoreConcurrencyShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_Translates_Concurrency_Into_The_Identity_Error(bool role) {
        var userMutation = new Mock<IResourceMutation<SchemataUser>>();
        userMutation.Setup(m => m.UpdateAsync(It.IsAny<SchemataUser>(), It.IsAny<IUnitOfWork>(), Operations.Update, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataUser>());
        var roleMutation = new Mock<IResourceMutation<SchemataRole>>();
        roleMutation.Setup(m => m.UpdateAsync(It.IsAny<SchemataRole>(), It.IsAny<IUnitOfWork>(), Operations.Update, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataRole>());

        IdentityResult result;
        if (role) {
            result = await CreateRoleStore(roleMutation).UpdateAsync(new SchemataRole { Name = "r" });
        } else {
            result = await CreateUserStore(userMutation).UpdateAsync(new SchemataUser { Name = "u" });
        }

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(IdentityErrorDescriber.ConcurrencyFailure), error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_Translates_Concurrency_Into_The_Identity_Error(bool role) {
        var userMutation = new Mock<IResourceMutation<SchemataUser>>();
        userMutation.Setup(m => m.DeleteAsync(It.IsAny<SchemataUser>(), It.IsAny<IUnitOfWork>(), Operations.Delete, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataUser>());
        var roleMutation = new Mock<IResourceMutation<SchemataRole>>();
        roleMutation.Setup(m => m.DeleteAsync(It.IsAny<SchemataRole>(), It.IsAny<IUnitOfWork>(), Operations.Delete, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataRole>());

        IdentityResult result;
        if (role) {
            result = await CreateRoleStore(roleMutation).DeleteAsync(new SchemataRole { Name = "r" });
        } else {
            result = await CreateUserStore(userMutation).DeleteAsync(new SchemataUser { Name = "u" });
        }

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(IdentityErrorDescriber.ConcurrencyFailure), error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_Propagates_Non_Concurrency_Failure(bool role) {
        var failure = new InvalidOperationException("boom");
        var userMutation = new Mock<IResourceMutation<SchemataUser>>();
        userMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataUser>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);
        var roleMutation = new Mock<IResourceMutation<SchemataRole>>();
        roleMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataRole>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);

        InvalidOperationException thrown;
        if (role) {
            thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateRoleStore(roleMutation).CreateAsync(new SchemataRole { Name = "r" }));
        } else {
            thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateUserStore(userMutation).CreateAsync(new SchemataUser { Name = "u" }));
        }

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task Redeem_Code_Updates_The_Recovery_Row_And_Removes_Only_The_Redeemed_Code() {
        var (store, tokens, entry) = CreateRedeemingStore("alpha;bravo");

        var redeemed = await store.RedeemCodeAsync(new SchemataUser { Name = "u" }, "alpha", CancellationToken.None);

        Assert.True(redeemed);
        Assert.Equal("bravo", entry.Value);
        tokens.Verify(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
    }

    [Fact]
    public async Task Redeem_Code_Returns_False_When_The_Concurrent_Update_Loses_The_Stamp() {
        var (store, _, _) = CreateRedeemingStore("alpha;bravo", lostStamp: true);

        var redeemed = await store.RedeemCodeAsync(new SchemataUser { Name = "u" }, "alpha", CancellationToken.None);

        Assert.False(redeemed);
    }

    [Fact]
    public async Task Redeem_Code_Returns_False_When_The_Recovery_Update_Writes_Nothing() {
        var (store, tokens, entry) = CreateRedeemingStore("alpha;bravo");
        tokens.Setup(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
              .ReturnsAsync(MutationResult.NoWrite);
        var transaction = new Mock<IUnitOfWork>();
        tokens.Setup(r => r.Begin()).Returns(transaction.Object);

        var redeemed = await store.RedeemCodeAsync(new SchemataUser { Name = "u" }, "alpha", CancellationToken.None);

        Assert.False(redeemed);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (SeededUserStore Store, Mock<IRepository<SchemataUserToken>> Tokens, SchemataUserToken Entry)
        CreateRedeemingStore(string codes, bool lostStamp = false) {
        var entry = new SchemataUserToken { LoginProvider = "store", Name = "codes", Value = codes };
        var tokens = new Mock<IRepository<SchemataUserToken>>();
        var unit = new Mock<IUnitOfWork>();
        tokens.Setup(r => r.Begin()).Returns(unit.Object);
        if (lostStamp) {
            tokens.Setup(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ThrowsAsync(new AbortedException());
        } else {
            tokens.Setup(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        }

        return (new SeededUserStore(tokens, entry, Mock.Of<IResourceMutation<SchemataUser>>()), tokens, entry);
    }

    private sealed class SeededUserStore(
        Mock<IRepository<SchemataUserToken>> tokens,
        SchemataUserToken                    entry,
        IResourceMutation<SchemataUser>      mutation
    ) : SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>(
        Mock.Of<IRepository<SchemataUserClaim>>(),
        Mock.Of<IRepository<SchemataUserRole>>(),
        Mock.Of<IRepository<SchemataUserLogin>>(),
        tokens.Object,
        mutation,
        Mock.Of<IServiceProvider>()
    )
    {
        protected override Task<SchemataUserToken?> FindTokenAsync(
            SchemataUser      user,
            string            loginProvider,
            string            name,
            CancellationToken ct
        ) {
            return Task.FromResult<SchemataUserToken?>(entry);
        }
    }

    private static SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin, SchemataUserToken, SchemataRoleClaim> CreateUserStore(Mock<IResourceMutation<SchemataUser>> mutation) {
        var users = new Mock<IRepository<SchemataUser>>();
        users.Setup(repository => repository.Begin(IsolationLevel.Serializable)).Returns(Mock.Of<IUnitOfWork>());
        var services = new ServiceCollection().AddSingleton(users.Object)
                                              .AddSingleton<ILookupNormalizer>(new UpperInvariantLookupNormalizer())
                                              .BuildServiceProvider();
        return new(
            Mock.Of<IRepository<SchemataUserClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(),
            Mock.Of<IRepository<SchemataUserLogin>>(),
            Mock.Of<IRepository<SchemataUserToken>>(),
            mutation.Object,
            services
        );
    }

    private static SchemataRoleStore<SchemataRole, SchemataRoleClaim, SchemataUserRole> CreateRoleStore(Mock<IResourceMutation<SchemataRole>> mutation) {
        var roles = new Mock<IRepository<SchemataRole>>();
        roles.Setup(repository => repository.Begin(IsolationLevel.Serializable)).Returns(Mock.Of<IUnitOfWork>());
        var services = new ServiceCollection().AddSingleton(roles.Object).BuildServiceProvider();
        return new(
            Mock.Of<IRepository<SchemataRoleClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(),
            mutation.Object,
            services
        );
    }
}

[Trait("Layer", "Integration")]
public sealed class UserStoreContactRaceShould
{
    [Theory]
    [InlineData(false, "email")]
    [InlineData(false, "phone")]
    [InlineData(false, "username")]
    [InlineData(true, "email")]
    [InlineData(true, "phone")]
    [InlineData(true, "username")]
    public async Task ConcurrentCreates_ForAContact_LeaveOneDurableMatch(bool linq, string contact) {
        await using var host = await ContactHost.CreateAsync(linq);

        var attempts = await Task.WhenAll(
            CreateAsync(host, contact, "first"),
            CreateAsync(host, contact, "second"));

        Assert.All(attempts, AssertAllowedCreate);
        Assert.Equal(1, await host.CountContactAsync(contact, SharedContact(contact)));
    }

    [Theory]
    [InlineData(false, "email")]
    [InlineData(false, "phone")]
    [InlineData(false, "username")]
    [InlineData(true, "email")]
    [InlineData(true, "phone")]
    [InlineData(true, "username")]
    public async Task ConcurrentUpdates_ForAContact_LeaveOneDurableMatch(bool linq, string contact) {
        await using var host = await ContactHost.CreateAsync(linq);
        var first = await CreateAsync(host, contact, "first", uniqueContact: true);
        var second = await CreateAsync(host, contact, "second", uniqueContact: true);
        Assert.Null(first.Error);
        Assert.Null(second.Error);
        var firstUid = await host.UidAsync("first");
        var secondUid = await host.UidAsync("second");

        var attempts = await Task.WhenAll(
            UpdateAsync(host, firstUid, contact),
            UpdateAsync(host, secondUid, contact));

        Assert.All(attempts, AssertAllowedUpdate);
        Assert.Equal(1, await host.CountContactAsync(contact, SharedContact(contact)));
    }

    private static string SharedContact(string contact) {
        return contact switch {
            "email" => "SHARED@EXAMPLE.COM",
            "phone" => "+15550001111",
            _ => "shared-user",
        };
    }

    private static async Task<(IdentityResult? Result, Exception? Error)> CreateAsync(
        ContactHost host,
        string contact,
        string name,
        bool uniqueContact = false) {
        await using var scope = host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var user = new SchemataUser {
            Name = name,
            UserName = contact == "username" && !uniqueContact ? SharedContact(contact) : name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = contact == "email"
                ? uniqueContact ? $"{name}@example.com" : SharedContact(contact)
                : null,
            PhoneNumber = contact == "phone"
                ? uniqueContact ? name == "first" ? "+15550000001" : "+15550000002" : SharedContact(contact)
                : null,
            SecurityStamp = Guid.NewGuid().ToString("n"),
            ConcurrencyStamp = Guid.NewGuid().ToString("n"),
            Timestamp = Guid.NewGuid(),
        };
        try {
            return (await manager.CreateAsync(user), null);
        } catch (Exception exception) {
            return (null, exception);
        }
    }

    private static async Task<(IdentityResult? Result, Exception? Error)> UpdateAsync(
        ContactHost host,
        Guid uid,
        string contact) {
        await using var scope = host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
        var user = await users.FindAsync([uid]);
        Assert.NotNull(user);
        if (contact == "email") user.Email = SharedContact(contact);
        else if (contact == "phone") user.PhoneNumber = SharedContact(contact);
        else user.UserName = SharedContact(contact);
        try {
            return (await manager.UpdateAsync(user), null);
        } catch (Exception exception) {
            return (null, exception);
        }
    }

    private static void AssertAllowedCreate((IdentityResult? Result, Exception? Error) attempt) {
        if (attempt.Result is { } result) Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        else Assert.True(attempt.Error is AlreadyExistsException or AbortedException, attempt.Error?.ToString());
    }

    private static void AssertAllowedUpdate((IdentityResult? Result, Exception? Error) attempt) {
        if (attempt.Result is { Succeeded: false } result) {
            var error = Assert.Single(result.Errors);
            Assert.Equal(nameof(IdentityErrorDescriber.ConcurrencyFailure), error.Code);
            return;
        }
        AssertAllowedCreate(attempt);
    }

    private sealed class ContactContext(DbContextOptions<ContactContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<SchemataUser>(); }
    }

    private sealed class ContactConnection(DataOptions options) : DataConnection(options);

    private sealed class ContactHost(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<ContactHost> CreateAsync(bool linq) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-contact-race-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddSingleton<ILookupNormalizer>(new UpperInvariantLookupNormalizer());
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<ContactConnection>>(_ => () => new(options));
                services.AddRepository<SchemataUser, LinqToDbRepository<ContactConnection, SchemataUser>>();
                using var db = new ContactConnection(options);
                db.CreateTable<SchemataUser>();
            } else {
                services.AddDbContextFactory<ContactContext>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<SchemataUser, EfCoreRepository<ContactContext, SchemataUser>>();
            }
            var registry = new ResourceRegistry();
            registry.Add(new(typeof(SchemataUser)), []);
            services.TryAddSingleton(registry);
            services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
            services.TryAddScoped<IResourceMutation<SchemataUser>, ResourceMutation<SchemataUser>>();
            services.AddSingleton(Mock.Of<IRepository<SchemataUserClaim>>());
            services.AddSingleton(Mock.Of<IRepository<SchemataUserRole>>());
            services.AddSingleton(Mock.Of<IRepository<SchemataUserLogin>>());
            services.AddSingleton(Mock.Of<IRepository<SchemataUserToken>>());
            services.AddScoped<SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole,
                SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>>();
            services.AddScoped<IUserStore<SchemataUser>>(provider => provider.GetRequiredService<
                SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole,
                    SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>>());
            services.AddScoped<IEmailNormalizer, DefaultEmailNormalizer>();
            services.AddScoped<IPhoneNumberNormalizer, E164PhoneNumberNormalizer>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped(
                typeof(IIdentityAliasUniquenessAdvisor<>), typeof(AdviceAliasUniqueness<>)));
            services.AddIdentityCore<SchemataUser>().AddUserManager<SchemataUserManager<SchemataUser>>();
            services.RemoveAll<IUserValidator<SchemataUser>>();
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<ContactContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
            return new(root, path);
        }

        public async Task<Guid> UidAsync(string name) {
            await using var scope = Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
            var user = await users.SingleOrDefaultAsync(query => query.Where(row => row.Name == name));
            Assert.NotNull(user);
            return user.Uid;
        }

        public async Task<int> CountContactAsync(string contact, string value) {
            await using var scope = Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
            if (contact == "username") {
                return await users.CountAsync(query => query.Where(row => row.NormalizedUserName == value.ToUpperInvariant()));
            }
            var expected = contact == "email" ? value : value.Substring(1);
            return contact == "email"
                ? await users.CountAsync(query => query.Where(row => row.NormalizedEmail == expected))
                : await users.CountAsync(query => query.Where(row => row.PhoneNumber == expected));
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
