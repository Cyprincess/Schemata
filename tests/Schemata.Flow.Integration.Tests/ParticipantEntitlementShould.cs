using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
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
        var fixture = new TenantFlowFixture(linq);
        var lifecycle = (IAsyncLifetime)fixture;
        await lifecycle.InitializeAsync();
        try {
            var tenant = Guid.NewGuid();
            var otherTenant = Guid.NewGuid();
            await using var scope = await fixture.EnterTenantAsync(tenant);
            var services = scope.ServiceProvider;
            var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
            var time = new Mock<TimeProvider>();
            time.Setup(provider => provider.GetUtcNow()).Returns(now);
            var resolver = new Mock<IFlowSubjectResolver>();
            resolver.Setup(value => value.ResolveAsync(It.IsAny<ClaimsPrincipal>(), default))
                .ReturnsAsync(new FlowSubjects("users/actor", ["groups/reviewer"]));
            var policy = new FlowAccessPolicy<TenantProcess, TenantProcessToken, TenantProcessParticipant>(services, resolver.Object, new DefaultPermissionResolver(),
                new DefaultPermissionMatcher(Options.Create(new SchemataSecurityOptions { PermissionClaimType = "permission" })), time.Object);
            foreach (var name in new[] { "allowed", "expired", "revoked", "foreign", "hidden" }) {
                await using var seedScope = await fixture.EnterTenantAsync(name == "foreign" ? otherTenant : tenant);
                var processes = seedScope.ServiceProvider.GetRequiredService<IRepository<TenantProcess>>();
                var rows = seedScope.ServiceProvider.GetRequiredService<IRepository<TenantProcessParticipant>>();
                await using var seed = processes.Begin();
                rows.Join(seed);
                await processes.AddAsync(new() { Name = name, DefinitionName = "test", DefinitionVersion = "1" });
                if (name != "hidden") {
                    await rows.AddAsync(new() {
                        Process = "processes/" + name, Subject = "groups/reviewer",
                        Kind = ProcessParticipationKind.Participation,
                        ExpiresAt = name == "expired" ? now.UtcDateTime : now.UtcDateTime.AddSeconds(1),
                        RevokedAt = name == "revoked" ? now.UtcDateTime : null,
                    });
                }
                await seed.CommitAsync();
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "external")], "test"));
            var read = new FlowReadPolicy<TenantProcess, ListRequest, TenantProcess, TenantProcessToken, TenantProcessParticipant>(policy);
            var expression = await read.GenerateEntitlementExpressionAsync(new() { Operation = nameof(Operations.List) }, principal);
            var query = services.GetRequiredService<IRepository<TenantProcess>>();
            Assert.Equal(1, await query.CountAsync(q => q.Where(expression!)));
            var page = await query.ListAsync(q => q.Where(expression!).OrderBy(p => p.Name).Take(1)).ToListAsync();
            Assert.Equal("allowed", Assert.Single(page).Name);
            principal.AddIdentity(new ClaimsIdentity([new Claim("permission", "tenant-process.list")], "test"));
            expression = await read.GenerateEntitlementExpressionAsync(new() { Operation = nameof(Operations.List) }, principal);
            Assert.Equal(4, await query.CountAsync(q => q.Where(expression!)));
        } finally { await lifecycle.DisposeAsync(); }
    }
}
