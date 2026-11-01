using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Mutations;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

/// <summary>
///     Pins the contract of the custom-store <c>UseIdentity</c> overload: it closes the delete
///     cascade on the built-in relation entities, and a host that persists its own relation
///     entities supplies the mutation owner explicitly.
/// </summary>
[Trait("Layer", "Integration")]
public class CustomIdentityStoreRegistrationShould
{
    [Fact]
    public void Close_The_Custom_Store_Overload_On_The_Built_In_Relation_Entities() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseIdentity<SchemataUser, SchemataRole,
            SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
                SchemataUserToken, SchemataRoleClaim>,
            SchemataRoleStore<SchemataRole, SchemataRoleClaim, SchemataUserRole>>());
        using var app   = builder.Build();
        using var scope = app.Services.CreateScope();

        Assert.IsType<UserResourceMutation<SchemataUser, SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
            SchemataUserToken>>(scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataUser>>());
        Assert.IsType<RoleResourceMutation<SchemataRole, SchemataRoleClaim, SchemataUserRole>>(
            scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataRole>>());
    }

    [Fact]
    public void Preserve_A_Host_Mutation_Owner_For_Custom_Relation_Entities() {
        var userOwner = Mock.Of<IResourceMutation<SchemataUser>>();
        var roleOwner = Mock.Of<IResourceMutation<SchemataRole>>();
        var builder   = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schema.Services.AddScoped(_ => userOwner);
            schema.Services.AddScoped(_ => roleOwner);
            schema.UseIdentity<SchemataUser, SchemataRole,
                SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
                    SchemataUserToken, SchemataRoleClaim>,
                SchemataRoleStore<SchemataRole, SchemataRoleClaim, SchemataUserRole>>();
        });
        using var app   = builder.Build();
        using var scope = app.Services.CreateScope();

        Assert.Same(userOwner, scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataUser>>());
        Assert.Same(roleOwner, scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataRole>>());
    }
}
