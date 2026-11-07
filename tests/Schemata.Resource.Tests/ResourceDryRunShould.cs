using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Caching.Skeleton;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Schemata.Validation.Skeleton.Advisors;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceDryRunShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public Task ValidateOnly_MissingUpdateTarget_MatchesLiveNotFound() {
        return AssertSameFailure<NotFoundException>(
            () => new Harness(),
            dryRun => ValidRequest(dryRun),
            (host, request) => host.Update("widgets/missing", request));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public Task ValidateOnly_DeniedInstance_MatchesLivePermissionDenied() {
        return AssertSameFailure<PermissionDeniedException>(
            () => new Harness(StoredEntity(), denied: true),
            dryRun => ValidRequest(dryRun),
            (host, request) => host.Update("widgets/w1", request));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public Task ValidateOnly_StaleEntityTag_MatchesLiveConcurrencyFailure() {
        return AssertSameFailure<AbortedException>(
            () => new Harness(StoredEntity(), freshness: true),
            dryRun => ValidRequest(dryRun, request => request.EntityTag = "stale"),
            (host, request) => host.Update("widgets/w1", request));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public Task ValidateOnly_InvalidMask_MatchesLiveValidationFailure() {
        return AssertSameFailure<ValidationException>(
            () => new Harness(StoredEntity()),
            dryRun => ValidRequest(dryRun, request => request.UpdateMask = "unknown_field"),
            (host, request) => host.Update("widgets/w1", request),
            "update_mask");
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public Task ValidateOnly_InvalidRequest_MatchesLiveValidationFailure() {
        return AssertSameFailure<ValidationException>(
            () => new Harness(),
            dryRun => new() { ValidateOnly = dryRun, CanonicalName = "widgets/w1" },
            (host, request) => host.Create(request),
            nameof(Request.DisplayName));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ValidateOnly_InvalidEntity_MatchesLiveValidationFailure() {
        var liveHost = new Harness();
        var dryHost  = new Harness();
        var entity   = new Entity { DisplayName = "invalid-entity", CanonicalName = "widgets/w1" };

        var live = await Record.ExceptionAsync(() => new AdviceAddValidation<Entity>().AdviseAsync(
            liveHost.Advice, liveHost.Repository.Object, entity, new(), CancellationToken.None));
        var dryRun = await Record.ExceptionAsync(() => dryHost.Create(
            ValidRequest(true, request => request.DisplayName = "invalid-entity")));

        Assert.IsType<ValidationException>(live);
        Assert.IsType<ValidationException>(dryRun);
        Assert.Equal(live.Message, dryRun.Message);
        Assert.Equal(nameof(Entity.DisplayName), ViolationField(live));
        Assert.Equal(nameof(Entity.DisplayName), ViolationField(dryRun));
        AssertNoWrites(liveHost);
        AssertNoWrites(dryHost);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public Task ValidateOnly_DuplicateCanonicalNameAfterSanitize_MatchesLiveCreate() {
        return AssertPipelineCreateFailure<AlreadyExistsException>(duplicate: true, leaf: "w1");
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public Task ValidateOnly_MissingCanonicalLeafAfterSanitize_MatchesLiveCreate() {
        return AssertPipelineCreateFailure<ValidationException>(duplicate: false, leaf: null, violationField: "name");
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ValidateOnly_Create_ExecutesLiveChecksWithoutWrites() {
        var host      = new Harness();
        var execution = new ResourceDispatchExecution();

        var result = await host.Create(ValidRequest(true), execution);

        Assert.Null(result.Detail);
        AssertSuccess(host, execution, Operations.Create, expectUniqueness: true);
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ValidateOnly_Update_ExecutesLiveChecksWithoutWritesOrAddedUniqueness() {
        var host      = new Harness(StoredEntity());
        var execution = new ResourceDispatchExecution();

        var result = await host.Update("widgets/w1", ValidRequest(true), execution);

        Assert.Null(result.Detail);
        AssertSuccess(host, execution, Operations.Update, expectUniqueness: false);
        host.Mutation.Verify(m => m.UpdateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ValidateOnly_AllowMissing_UsesCreateChecksWithoutWrites() {
        var host      = new Harness();
        var execution = new ResourceDispatchExecution();
        var request   = ValidRequest(true, value => value.AllowMissing = true);

        var result = await host.Update("widgets/w1", request, execution);

        Assert.Null(result.Detail);
        AssertSuccess(host, execution, Operations.Create, expectUniqueness: true, requestOperation: Operations.Update);
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task AssertPipelineCreateFailure<TException>(
        bool    duplicate,
        string? leaf,
        string? violationField = null
    )
        where TException : Exception {
        using var liveHost = new PipelineHost(duplicate);
        using var dryHost  = new PipelineHost(duplicate);

        var live   = await Record.ExceptionAsync(() => liveHost.Create(leaf, validateOnly: false));
        var dryRun = await Record.ExceptionAsync(() => dryHost.Create(leaf, validateOnly: true));

        Assert.IsType<TException>(live);
        Assert.IsType<TException>(dryRun);
        Assert.Equal(live.Message, dryRun.Message);
        if (live is SchemataException liveError && dryRun is SchemataException dryError) {
            Assert.Equal(liveError.Code, dryError.Code);
            Assert.Equal(liveError.Status, dryError.Status);
        }

        if (violationField is not null) {
            Assert.Equal(violationField, ViolationField(live));
            Assert.Equal(violationField, ViolationField(dryRun));
        }

        dryHost.Repository.Verify(r => r.Begin(), Times.Never);
        dryHost.Repository.Verify(r => r.AddAsync(
            It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
        dryHost.Unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Request ValidRequest(bool dryRun, Action<Request>? configure = null) {
        var request = new Request {
            ValidateOnly  = dryRun,
            DisplayName   = "Widget",
            Name          = "w1",
            CanonicalName = "widgets/w1",
        };
        configure?.Invoke(request);
        return request;
    }

    private static Entity StoredEntity() {
        return new() {
            DisplayName   = "Stored",
            Name          = "w1",
            CanonicalName = "widgets/w1",
            Timestamp     = Guid.NewGuid(),
        };
    }

    private static async Task AssertSameFailure<TException>(
        Func<Harness>                         compose,
        Func<bool, Request>                   request,
        Func<Harness, Request, Task>          invoke,
        string?                               violationField = null
    )
        where TException : Exception {
        var liveHost = compose();
        var dryHost  = compose();
        var live     = await Record.ExceptionAsync(() => invoke(liveHost, request(false)));
        var dryRun   = await Record.ExceptionAsync(() => invoke(dryHost, request(true)));

        Assert.IsType<TException>(live);
        Assert.IsType<TException>(dryRun);
        Assert.Equal(live.Message, dryRun.Message);
        if (live is SchemataException liveError && dryRun is SchemataException dryError) {
            Assert.Equal(liveError.Code, dryError.Code);
            Assert.Equal(liveError.Status, dryError.Status);
        }

        if (violationField is not null) {
            Assert.Equal(violationField, ViolationField(live));
            Assert.Equal(violationField, ViolationField(dryRun));
        }

        AssertNoWrites(liveHost);
        AssertNoWrites(dryHost);
    }

    private static string? ViolationField(Exception error) {
        var validation = Assert.IsType<ValidationException>(error);
        var detail     = Assert.Single(validation.Details!.OfType<BadRequestDetail>());
        return Assert.Single(detail.FieldViolations!).Field;
    }

    private static void AssertSuccess(
        Harness                  host,
        ResourceDispatchExecution execution,
        Operations               operation,
        bool                     expectUniqueness,
        Operations?              requestOperation = null
    ) {
        Assert.Equal(requestOperation ?? operation, host.RequestOperation);
        Assert.Equal(operation, host.EntityOperation);
        Assert.Equal(1, host.RequestCalls);
        Assert.Equal(1, host.EntityCalls);
        Assert.Equal(ResourceDispatchOutcome.NotWritten, execution.Outcome);
        host.Repository.Verify(r => r.GetAsync(
            It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()),
            expectUniqueness ? Times.Once() : Times.Never());
        AssertNoWrites(host);
    }

    private static void AssertNoWrites(Harness host) {
        host.Repository.Verify(r => r.Begin(), Times.Never);
        host.Unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        host.Mutation.Verify(m => m.CreateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Mutation.Verify(m => m.UpdateAsync(
            It.IsAny<Entity>(), It.IsAny<IUnitOfWork>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Committed.Verify(a => a.Prepare(It.IsAny<Entity>(), It.IsAny<Operations>()), Times.Never);
    }

    private sealed class PipelineHost : IDisposable
    {
        public PipelineHost(bool duplicate) {
            Unit       = UnitOfWorkMock.Create();
            Repository = UnitOfWorkMock.Repository<Entity>(Unit);
            ServiceProvider? provider = null;
            Repository.SetupGet(r => r.AdviceContext).Returns(() => new AdviceContext(provider!));
            Repository.Setup(r => r.GetAsync(
                                  It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns((Entity? entity, CancellationToken _, QueryOptions _) =>
                          new ValueTask<Entity?>(duplicate && entity?.CanonicalName == "widgets/w1"
                              ? StoredEntity()
                              : null));
            Repository.Setup(r => r.AddAsync(
                                  It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                      .Returns(async (Entity entity, CancellationToken ct, MutationOptions options) => {
                          var result = await Advisor.For<IRepositoryAddAdvisor<Entity>>()
                                                    .RunAsync(Repository.Object.AdviceContext,
                                                        Repository.Object, entity, options, ct);
                          return result == AdviseResult.Continue ? MutationResult.Applied : MutationResult.NoWrite;
                      });

            var mapper = new Mock<ISimpleMapper>();
            mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>()))
                  .Returns((Request source) => new Entity {
                      DisplayName   = source.DisplayName,
                      Name          = source.Name,
                      CanonicalName = source.CanonicalName,
                  });

            var services = new ServiceCollection();
            var options  = new SchemataOptions();
            services.AddSchemataResources(options);
            new SchemataResourceBuilder(options, services).Use<Entity, Request, Detail, Summary>();
            services.AddSingleton(Mock.Of<ICacheProvider>());
            services.AddSingleton(Repository.Object);
            services.AddSingleton(mapper.Object);
            services.AddScoped<IResourceMutation<Entity>, ResourceMutation<Entity>>();
            services.AddSingleton<IResourceCreateAdvisor<Entity, Request, Detail>, ApplyCreateName>();
            services.AddSingleton<IRepositoryAddAdvisor<Entity>, AdviceAddCanonicalName<Entity>>();
            services.AddSingleton<IRepositoryAddAdvisor<Entity>, AdviceAddValidation<Entity>>();
            services.AddSingleton<IRepositoryAddAdvisor<Entity>, AdviceAddUniqueness<Entity>>();
            provider = services.BuildServiceProvider();
            Provider = provider;
        }

        public Mock<IRepository<Entity>> Repository { get; }
        public Mock<IUnitOfWork> Unit { get; }
        private ServiceProvider Provider { get; }

        public async Task<CreateResultBase<Detail>> Create(string? leaf, bool validateOnly) {
            using var scope = Provider.CreateScope();
            var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);
            return await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
                new(new() {
                    DisplayName   = leaf,
                    Name          = "client-name",
                    CanonicalName = "widgets/client-name",
                    ValidateOnly  = validateOnly,
                }, null));
        }

        public void Dispose() { Provider.Dispose(); }
    }

    private sealed class ApplyCreateName : IResourceCreateAdvisor<Entity, Request, Detail>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                       ctx,
            Request                             request,
            Entity                              entity,
            System.Security.Claims.ClaimsPrincipal? principal,
            HandledResponse<CreateResultBase<Detail>> handled,
            CancellationToken                   ct = default
        ) {
            Assert.Null(request.Name);
            Assert.Null(request.CanonicalName);
            entity.Name = request.DisplayName;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class Harness
    {
        public Harness(Entity? stored = null, bool duplicate = false, bool denied = false, bool freshness = false) {
            Unit       = UnitOfWorkMock.Create();
            Repository = UnitOfWorkMock.Repository<Entity>(Unit);
            Mutation   = ResourceMutationMock.Create<Entity>();
            Committed  = new Mock<IResourceMutationCommittedAdvisor<Entity>>();
            Repository.Setup(r => r.SingleOrDefaultAsync(
                                  It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                                  It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns(new ValueTask<Entity?>(stored));
            Repository.Setup(r => r.GetAsync(
                                  It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns(new ValueTask<Entity?>(duplicate ? StoredEntity() : null));

            var mapper = new Mock<ISimpleMapper>();
            mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>()))
                  .Returns((Request source) => new Entity {
                      DisplayName   = source.DisplayName,
                      Name          = source.Name,
                      CanonicalName = source.CanonicalName,
                  });
            mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>(), It.IsAny<Entity>()))
                  .Callback((Request source, Entity destination) => destination.DisplayName = source.DisplayName);
            mapper.Setup(m => m.Map<Request, Entity>(
                       It.IsAny<Request>(), It.IsAny<Entity>(), It.IsAny<IEnumerable<string>>()))
                  .Callback((Request source, Entity destination, IEnumerable<string> _) =>
                       destination.DisplayName = source.DisplayName);

            var requestValidator = new Mock<IValidationAdvisor<Request>>();
            requestValidator.SetupGet(v => v.Order).Returns(0);
            requestValidator.Setup(v => v.AdviseAsync(
                                        It.IsAny<AdviceContext>(), It.IsAny<Operations>(), It.IsAny<Request>(),
                                        It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
                            .Callback<AdviceContext, Operations, Request, IList<ErrorFieldViolation>, CancellationToken>(
                                (_, operation, value, errors, _) => {
                                    RequestCalls++;
                                    RequestOperation = operation;
                                    if (string.IsNullOrEmpty(value.DisplayName)) {
                                        errors.Add(new() {
                                            Field       = nameof(Request.DisplayName),
                                            Description = "Display name is required.",
                                        });
                                    }
                                })
                            .Returns((AdviceContext _, Operations _, Request value,
                                      IList<ErrorFieldViolation> _, CancellationToken _) =>
                                Task.FromResult(string.IsNullOrEmpty(value.DisplayName)
                                    ? AdviseResult.Block
                                    : AdviseResult.Continue));

            var entityValidator = new Mock<IValidationAdvisor<Entity>>();
            entityValidator.SetupGet(v => v.Order).Returns(0);
            entityValidator.Setup(v => v.AdviseAsync(
                                       It.IsAny<AdviceContext>(), It.IsAny<Operations>(), It.IsAny<Entity>(),
                                       It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
                           .Callback<AdviceContext, Operations, Entity, IList<ErrorFieldViolation>, CancellationToken>(
                               (_, operation, value, errors, _) => {
                                   EntityCalls++;
                                   EntityOperation = operation;
                                   if (value.DisplayName == "invalid-entity") {
                                       errors.Add(new() {
                                           Field       = nameof(Entity.DisplayName),
                                           Description = "Entity is invalid.",
                                       });
                                   }
                               })
                           .Returns((AdviceContext _, Operations _, Entity value,
                                     IList<ErrorFieldViolation> _, CancellationToken _) =>
                               Task.FromResult(value.DisplayName == "invalid-entity"
                                   ? AdviseResult.Block
                                   : AdviseResult.Continue));

            var services = new ServiceCollection();
            services.AddSingleton(requestValidator.Object);
            services.AddSingleton(entityValidator.Object);
            services.AddSingleton(Mutation.Object);
            services.AddSingleton(Committed.Object);
            if (denied) {
                var access = new Mock<IAccessProvider<Entity, Request>>();
                access.Setup(a => a.HasAccessAsync(
                                   It.IsAny<Entity>(), It.IsAny<AccessContext<Request>>(), null,
                                   It.IsAny<CancellationToken>()))
                      .ReturnsAsync(AccessDecision.Denied);
                var permissions = new Mock<IPermissionResolver>();
                permissions.Setup(p => p.Resolve(It.IsAny<string>(), It.IsAny<Type>())).Returns("widgets.update");
                services.AddSingleton<IResourceUpdateAdvisor<Entity, Request, Detail>>(
                    new ResourceUpdateAccessAdvisor<Entity, Request, Detail>(
                        access.Object, permissions.Object));
            }

            if (freshness) {
                services.AddSingleton<IResourceUpdateAdvisor<Entity, Request, Detail>>(
                    new AdviceUpdateFreshness<Entity, Request, Detail>());
            }

            Provider = services.BuildServiceProvider();
            Advice   = new(Provider);
            Handler  = new(Provider, Repository.Object, mapper.Object);
        }

        public Mock<IRepository<Entity>> Repository { get; }
        public Mock<IUnitOfWork> Unit { get; }
        public Mock<IResourceMutation<Entity>> Mutation { get; }
        public Mock<IResourceMutationCommittedAdvisor<Entity>> Committed { get; }
        public Operations RequestOperation { get; private set; }
        public Operations EntityOperation { get; private set; }
        public int RequestCalls { get; private set; }
        public int EntityCalls { get; private set; }

        private ServiceProvider Provider { get; }
        public AdviceContext Advice { get; }
        private ResourceOperationHandler<Entity, Request, Detail, Summary> Handler { get; }

        public Task<CreateResultBase<Detail>> Create(
            Request                   request,
            ResourceDispatchExecution? execution = null
        ) {
            return new ResourceCreateValidationPipelineAdvisor<Entity, Request, Detail>().AdviseAsync(
                Advice,
                new(request, null),
                ct => Handler.CreateAsync(request, null, Advice, ct, execution),
                CancellationToken.None);
        }

        public Task<UpdateResultBase<Detail>> Update(
            string                     name,
            Request                    request,
            ResourceDispatchExecution? execution = null
        ) {
            return new ResourceUpdateValidationPipelineAdvisor<Entity, Request, Detail>().AdviseAsync(
                Advice,
                new(name, request, null),
                ct => Handler.UpdateAsync(name, request, null, Advice, ct, execution),
                CancellationToken.None);
        }
    }

    [CanonicalName("widgets/{widget}")]
    [Microsoft.EntityFrameworkCore.PrimaryKey(nameof(CanonicalName))]
    public sealed class Entity : ICanonicalName, IConcurrency
    {
        public string? DisplayName { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public Guid Timestamp { get; set; }
    }

    public sealed class Request : ICanonicalName, IValidation, IUpdateMask, IFreshness, IAllowMissing
    {
        public string? DisplayName { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public string? UpdateMask { get; set; }
        public string? EntityTag { get; set; }
        public bool ValidateOnly { get; set; }
        public bool AllowMissing { get; set; }
    }

    public sealed class Detail : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Summary : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }
}
