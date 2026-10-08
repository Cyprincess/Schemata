using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Integration")]
public class UserManagerSubjectShould
{
    [Theory]
    [InlineData("alice")]
    [InlineData("4be45926-e594-448d-8a47-94d5fa1ab7d2")]
    public async Task Canonical_Subject_Resolves_Name_Independently_Of_Database_Id(string name) {
        var owner = new SchemataUser {
            Uid = Guid.Parse("034125cc-9415-4201-a7e2-fcfd9b645e56"), Name = name, CanonicalName = "users/" + name,
        };
        var other = new SchemataUser {
            Uid = Guid.Parse("4be45926-e594-448d-8a47-94d5fa1ab7d2"), Name = "other", CanonicalName = "users/other",
        };
        var rows = new[] { owner, other }.AsQueryable();
        var repository = new Mock<IRepository<SchemataUser>>();
        repository.Setup(r => r.SingleOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataUser>, IQueryable<SchemataUser>>?>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataUser>, IQueryable<SchemataUser>> query, CancellationToken _) =>
                      ValueTask.FromResult(query(rows).SingleOrDefault()));
        using var services = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        using var store = new SchemataUserStore<SchemataUser>(Mock.Of<IRepository<SchemataUserClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(), Mock.Of<IRepository<SchemataUserLogin>>(),
            Mock.Of<IRepository<SchemataUserToken>>(), Mock.Of<IResourceMutation<SchemataUser>>(), services);
        var options = new IdentityOptions();
        options.ClaimsIdentity.UserIdClaimType = "sub";
        using var manager = new SchemataUserManager<SchemataUser>(services, store, Options.Create(options),
            new PasswordHasher<SchemataUser>(), [], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", owner.CanonicalName)], "ticket"));

        var resolved = await manager.GetUserAsync(principal);

        Assert.Same(owner, resolved);
        Assert.Same(other, await manager.GetUserAsync(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", other.Uid.ToString()),
        ], "ticket"))));
        Assert.Null(await manager.GetUserAsync(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "users/missing"),
        ], "ticket"))));
    }
}
