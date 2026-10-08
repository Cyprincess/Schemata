using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Authorization.Tests;

[Trait("Category", "Integration")]
public class IdentityBridgeShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolve_Explicit_User_Regardless_Of_Identity_And_Validator_Registration_Order(bool bridgeFirst) {
        var builder = WebApplication.CreateBuilder();
        var user = new SelectedUser { Uid = Guid.NewGuid(), Name = "selected", CanonicalName = "users/selected" };
        var store = new Mock<ISelectedStore>();
        store.Setup(s => s.FindByCanonicalNameAsync("users/selected", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        store.Setup(s => s.GetRolesAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "selected-role" });
        builder.UseSchemata(schema => {
            void Bridge() => schema.UseAuthorization(o => o.Issuer = "https://as.example").UseIdentity<SelectedUser>();
            if (bridgeFirst) Bridge();
            schema.UseIdentity<SelectedUser, SchemataRole>();
            schema.Services.AddSingleton(Mock.Of<IUserValidator<SchemataUser>>());
            schema.Services.AddSingleton(Mock.Of<IUserValidator<SelectedUser>>());
            if (!bridgeFirst) Bridge();
        });
        builder.Services.AddScoped(sp => new SchemataUserManager<SelectedUser>(sp, store.Object,
            Options.Create(new IdentityOptions()), new PasswordHasher<SelectedUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new(), NullLogger<SchemataUserManager<SelectedUser>>.Instance));
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var subjects = scope.ServiceProvider.GetRequiredService<ISubjectProvider>();
        var claims = await subjects.GetClaimsAsync("users/selected");
        Assert.Contains(claims, c => c.Type == "sub" && c.Value == "users/selected");
        Assert.Contains(claims, c => c.Type == "role" && c.Value == "selected-role");
        Assert.False(await subjects.ValidateAsync("users/unknown"));
    }

    public sealed class SelectedUser : SchemataUser;
    public interface ISelectedStore : IUserStore<SelectedUser>, IUserCanonicalNameStore<SelectedUser>,
        IUserEmailStore<SelectedUser>, IUserPhoneNumberStore<SelectedUser>, IUserRoleStore<SelectedUser>,
        IUserDisplayNameStore<SelectedUser>, IUserPrincipalNameStore<SelectedUser>;
}
