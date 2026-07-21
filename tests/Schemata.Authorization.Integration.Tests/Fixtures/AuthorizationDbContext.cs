using Microsoft.EntityFrameworkCore;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

public class AuthorizationDbContext : DbContext
{
    public AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options) : base(options) { }

    public DbSet<SchemataApplication> Applications { get; set; } = null!;

    public DbSet<SchemataAuthorization> Authorizations { get; set; } = null!;

    public DbSet<SchemataScope> Scopes { get; set; } = null!;

    public DbSet<SchemataSubjectMapping> SubjectMappings { get; set; } = null!;

    public DbSet<SchemataToken> Tokens { get; set; } = null!;
}
