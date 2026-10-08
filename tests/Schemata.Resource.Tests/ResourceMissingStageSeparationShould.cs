using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests;

/// <summary>
///     Missing-target authorization is installed per entity (keyed by entity type): in one
///     container, the entity whose authorization was activated runs its access policy on a
///     missing target while the unactivated entity resolves straight to NOT_FOUND without any
///     policy consultation.
/// </summary>
public class ResourceMissingStageSeparationShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Get_MissingTarget_Runs_Policy_Only_For_The_Authorized_Entity() {
        var services = new ServiceCollection();

        // The same install fact the authorization wiring produces: the stage exists keyed by the
        // authorized entity only.
        services.AddKeyedScoped<ResourceAccessStage>(typeof(Authorized));

        var access = new Mock<IAccessProvider<Authorized, GetRequest>>();
        access.Setup(a => a.HasAccessAsync(
                      It.IsAny<Authorized?>(),
                      It.IsAny<AccessContext<GetRequest>>(),
                      It.IsAny<ClaimsPrincipal?>(),
                      It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Denied);
        services.AddSingleton(access.Object);

        using var provider = services.BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(provider));

        // The unactivated entity resolves straight to NOT_FOUND; the policy is never consulted,
        // and no cross-entity resolution error leaks.
        var plainRepository = AbsentLoad<Plain>();
        var plain = new ResourceOperationHandler<Plain, Request, Detail, Summary>(
            provider, plainRepository.Object, new Mock<ISimpleMapper>(MockBehavior.Loose).Object);
        await Assert.ThrowsAsync<NotFoundException>(
            () => plain.GetAsync(new GetRequest { Name = "plains/p1" }, null, CancellationToken.None));
        access.VerifyNoOtherCalls();

        // The activated entity runs the missing-stage policy; a definite denial fails as
        // PERMISSION_DENIED instead of disclosing the absence.
        var authorizedRepository = AbsentLoad<Authorized>();
        var authorized = new ResourceOperationHandler<Authorized, Request, Detail, Summary>(
            provider, authorizedRepository.Object, new Mock<ISimpleMapper>(MockBehavior.Loose).Object);
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => authorized.GetAsync(new GetRequest { Name = "authorizeds/a1" }, null, CancellationToken.None));
        access.Verify(a => a.HasAccessAsync(
                          null,
                          It.Is<AccessContext<GetRequest>>(c => c.Stage == AccessStage.Target
                                                             && c.Operation == nameof(Operations.Get)),
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IRepository<TEntity>> AbsentLoad<TEntity>() where TEntity : class, ICanonicalName {
        var repository = new Mock<IRepository<TEntity>>();
        repository.Setup(r => r.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                          It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<TEntity?>((TEntity?)null));

        return repository;
    }

    [CanonicalName("authorizeds/{authorized}")]
    public sealed class Authorized : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("plains/{plain}")]
    public sealed class Plain : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Detail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
