using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public sealed class ParticipantEntitlementShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_Expiry_Boundary_Revocation_Groups_And_Tenant_In_Provider_Query(bool linq) {
        IFlowIntegrationFixture fixture = linq ? new LinqToDbFlowFixture() : new EfCoreFlowFixture();
        var lifecycle = (IAsyncLifetime)fixture;
        await lifecycle.InitializeAsync();
        try {
            using var scope = fixture.CreateScope();
            var services = scope.ServiceProvider;
            var tenant = Guid.NewGuid();
            var otherTenant = Guid.NewGuid();
            using var entered = TenantContext.Enter(new(tenant));
            var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
            var time = new Mock<TimeProvider>();
            time.Setup(provider => provider.GetUtcNow()).Returns(now);
            var resolver = new Mock<IFlowSubjectResolver>();
            resolver.Setup(value => value.ResolveAsync(It.IsAny<ClaimsPrincipal>(), default))
                .ReturnsAsync(new FlowSubjects("users/actor", ["groups/reviewer"]));
            var policy = new FlowAccessPolicy(services, resolver.Object, new DefaultPermissionResolver(),
                new DefaultPermissionMatcher(Options.Create(new SchemataSecurityOptions { PermissionClaimType = "permission" })), time.Object);
            var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
            var rows = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
            await using var seed = processes.Begin();
            rows.Join(seed);
            foreach (var name in new[] { "allowed", "expired", "revoked", "foreign", "hidden" }) {
                await processes.AddAsync(new() { Name = name, DefinitionName = "test", DefinitionVersion = "1", TenantUid = name == "foreign" ? otherTenant : tenant });
                if (name == "hidden") continue;
                await rows.AddAsync(new() {
                    Process = "processes/" + name, Subject = "groups/reviewer", TenantUid = name == "foreign" ? otherTenant : tenant,
                    Kind = ProcessParticipationKind.Participation,
                    ExpiresAt = name == "expired" ? now.UtcDateTime : now.UtcDateTime.AddSeconds(1),
                    RevokedAt = name == "revoked" ? now.UtcDateTime : null,
                });
            }
            await seed.CommitAsync();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "external")], "test"));
            var read = new FlowReadPolicy<SchemataProcess, ListRequest>(policy);
            var expression = await read.GenerateEntitlementExpressionAsync(new() { Operation = nameof(Operations.List) }, principal);
            var query = services.GetRequiredService<IRepository<SchemataProcess>>();
            Assert.Equal(1, await query.CountAsync(q => q.Where(expression!)));
            var page = await query.ListAsync(q => q.Where(expression!).OrderBy(p => p.Name).Take(1)).ToListAsync();
            Assert.Equal("allowed", Assert.Single(page).Name);
            principal.AddIdentity(new ClaimsIdentity([new Claim("permission", "schemata-process.list")], "test"));
            expression = await read.GenerateEntitlementExpressionAsync(new() { Operation = nameof(Operations.List) }, principal);
            Assert.Equal(4, await query.CountAsync(q => q.Where(expression!)));
        } finally { await lifecycle.DisposeAsync(); }
    }
}
