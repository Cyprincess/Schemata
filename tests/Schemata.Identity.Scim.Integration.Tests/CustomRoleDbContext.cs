using Microsoft.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Scim.Integration.Tests;

public sealed class CustomRoleDbContext(DbContextOptions<CustomRoleDbContext> options) : DbContext(options)
{
    public DbSet<SchemataUser> Users { get; set; } = null!;
    public DbSet<CustomRole> Roles { get; set; } = null!;
    public DbSet<SchemataUserClaim> UserClaims { get; set; } = null!;
    public DbSet<SchemataUserRole> UserRoles { get; set; } = null!;
    public DbSet<SchemataUserLogin> UserLogins { get; set; } = null!;
    public DbSet<SchemataUserToken> UserTokens { get; set; } = null!;
    public DbSet<SchemataRoleClaim> RoleClaims { get; set; } = null!;
}
