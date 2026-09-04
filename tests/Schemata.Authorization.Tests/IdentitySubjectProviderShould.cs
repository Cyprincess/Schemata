using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Identity;
using Schemata.Authorization.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Authorization.Tests;

public class IdentitySubjectProviderShould
{
    [Fact]
    public async Task Resolve_Canonical_Subject_Through_Canonical_Name_Lookup() {
        var user = new SchemataUser {
            Uid = Guid.NewGuid(), Name = "aB3x9Q", CanonicalName = "users/aB3x9Q", UserName = "alice",
        };
        var store = NewStore();
        store.Setup(s => s.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new FormatException("Unrecognized Guid format."));
        store.Setup(s => s.FindByCanonicalNameAsync("users/aB3x9Q", It.IsAny<CancellationToken>()))
             .ReturnsAsync(user);

        var claims = await NewProvider(store).GetClaimsAsync("users/aB3x9Q");

        Assert.Contains(claims, c => c is { Type: "sub", Value: "users/aB3x9Q" });
        store.Verify(s => s.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Guid_Subject_Through_Id_Lookup() {
        var uid  = Guid.NewGuid();
        var user = new SchemataUser { Uid = uid, UserName = "alice" };
        var store = NewStore();
        store.Setup(s => s.FindByIdAsync(uid.ToString(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(user);

        var claims = await NewProvider(store).GetClaimsAsync($"users/{uid}");

        Assert.Contains(claims, c => c.Type == "sub" && c.Value == $"users/{uid}");
        store.Verify(s => s.FindByCanonicalNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_A_Guid_Leaf_Under_A_Foreign_Collection() {
        var uid   = Guid.NewGuid();
        var store = NewStore();
        store.Setup(s => s.FindByIdAsync(uid.ToString(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new SchemataUser { Uid = uid, UserName = "alice" });

        var valid = await NewProvider(store).ValidateAsync($"garbage/{uid}");

        Assert.False(valid);
        store.Verify(s => s.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_Subject_Yields_Empty_Claims_And_Failed_Validation() {
        var store    = NewStore();
        var provider = NewProvider(store);

        var claims = await provider.GetClaimsAsync("users/unknown");
        var valid  = await provider.ValidateAsync("users/unknown");

        Assert.Empty(claims);
        Assert.False(valid);
    }

    private static ISubjectProvider NewProvider(Mock<ICompositeUserStore> store) {
        var sp = new ServiceCollection().BuildServiceProvider();
        var manager = new SchemataUserManager<SchemataUser>(
            sp, store.Object, Options.Create(new IdentityOptions()), new PasswordHasher<SchemataUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        return new IdentitySubjectProvider<SchemataUser>(manager);
    }

    private static Mock<ICompositeUserStore> NewStore() {
        var store = new Mock<ICompositeUserStore>();
        store.Setup(s => s.GetUserPrincipalNameAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string?)null);
        store.Setup(s => s.GetEmailAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string?)null);
        store.Setup(s => s.GetPhoneNumberAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string?)null);
        store.Setup(s => s.GetDisplayNameAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string?)null);
        store.Setup(s => s.GetRolesAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<string>());
        return store;
    }

    #region Nested type: ICompositeUserStore

    public interface ICompositeUserStore : IUserStore<SchemataUser>, IUserCanonicalNameStore<SchemataUser>,
                                           IUserEmailStore<SchemataUser>, IUserPhoneNumberStore<SchemataUser>,
                                           IUserRoleStore<SchemataUser>, IUserDisplayNameStore<SchemataUser>,
                                           IUserPrincipalNameStore<SchemataUser>;

    #endregion
}
