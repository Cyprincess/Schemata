using Schemata.Core.Building;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Advisors;
using Schemata.Validation.Skeleton.Advisors;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceRequestPipelineAdvisorShould
{
    [Fact]
    public async Task Create_Sanitize_ClearsSystemFields_BeforeHandlerMaps() {
        var request = new Request {
            DisplayName = "keep-me",
            Name        = "entities/forged",
            CreateTime  = DateTime.UtcNow,
            UpdateTime  = DateTime.UtcNow,
            DeleteTime  = DateTime.UtcNow,
            PurgeTime   = DateTime.UtcNow,
        };

        var (repository, mapper) = CreateDoubles();
        Request? mapped = null;
        mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>()))
              .Callback((Request r) => mapped = r)
              .Returns(MappedEntity);

        using var services = BuildServices(repository.Object, mapper.Object, services => {
            services.AddSingleton<IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
                new ResourceCreateSanitizePipelineAdvisor<Entity, Request, Detail>());
        });
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(request, null), CancellationToken.None);

        Assert.Same(MappedDetail, result.Detail);
        // The handler receives the envelope's request instance: sanitize mutates it in place.
        Assert.Same(request, mapped);
        Assert.Null(mapped!.Name);
        Assert.Null(mapped.CreateTime);
        Assert.Null(mapped.UpdateTime);
        Assert.Null(mapped.DeleteTime);
        Assert.Null(mapped.PurgeTime);
        Assert.Equal("keep-me", mapped.DisplayName);
    }

    [Fact]
    public async Task Create_Validation_ThrowsValidationException_WithFieldViolations() {
        var request = new Request { DisplayName = "", CreateTime = DateTime.UtcNow };

        var (repository, mapper) = CreateDoubles();
        var validator = new RejectingValidationAdvisor();
        using var services = BuildServices(repository.Object, mapper.Object, services => {
            services.AddSingleton<IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
                new ResourceCreateSanitizePipelineAdvisor<Entity, Request, Detail>());
            services.AddSingleton<IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
                new ResourceCreateValidationPipelineAdvisor<Entity, Request, Detail>());
            services.AddSingleton<IValidationAdvisor<Request>>(validator);
        });
        var dispatcher = new InProcessRequestDispatcher(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
                new(request, null), CancellationToken.None));

        Assert.NotNull(ex.Details);
        var violation = Assert.Single(ex.Details.OfType<BadRequestDetail>().Single().FieldViolations!);
        Assert.Equal(nameof(Request.DisplayName), violation.Field);
        // Sanitize (SecurityOrders.Sanitize) runs ahead of validation (SecurityOrders.Validation):
        // the validator already sees the scrubbed payload.
        Assert.Null(validator.SeenCreateTime);
        mapper.Verify(m => m.Map<Request, Entity>(It.IsAny<Request>()), Times.Never);
    }

    [Fact]
    public async Task Create_SuppressedValidation_SkipsValidator() {
        var request = new Request { DisplayName = "" };

        var (repository, mapper) = CreateDoubles();
        var validator = new RejectingValidationAdvisor();
        using var services = BuildServices(
            repository.Object,
            mapper.Object,
            services => {
                services.AddSingleton<IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
                    new ResourceCreateValidationPipelineAdvisor<Entity, Request, Detail>());
                services.AddSingleton<IValidationAdvisor<Request>>(validator);
            },
            new() { SuppressCreateValidation = true });
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(request, null), CancellationToken.None);

        Assert.Same(MappedDetail, result.Detail);
        Assert.False(validator.Invoked);
    }

    [Fact]
    public async Task Update_Sanitize_ClearsSystemFields_AndStripsMask_BeforeHandlerMaps() {
        var request = new Request {
            DisplayName = "keep-me",
            Name        = "entities/forged",
            CreateTime  = DateTime.UtcNow,
            UpdateMask  = "display_name,create_time",
        };

        var (repository, mapper) = CreateDoubles();
        Request?             mapped       = null;
        IEnumerable<string>? mappedFields = null;
        mapper.Setup(m => m.Map<Request, Entity>(
                   It.IsAny<Request>(), It.IsAny<Entity>(), It.IsAny<IEnumerable<string>>()))
              .Callback((Request r, Entity e, IEnumerable<string> fields) => {
                   mapped       = r;
                   mappedFields = fields;
               });

        using var services = BuildServices(repository.Object, mapper.Object, services => {
            services.AddSingleton<IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(
                new ResourceUpdateSanitizePipelineAdvisor<Entity, Request, Detail>());
        });
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
            new("entities/e1", request, null), CancellationToken.None);

        Assert.Same(MappedDetail, result.Detail);
        Assert.Same(request, mapped);
        Assert.Null(mapped!.Name);
        Assert.Null(mapped.CreateTime);
        Assert.Equal("keep-me", mapped.DisplayName);
        Assert.Equal("display_name", request.UpdateMask);
        Assert.Equal(["DisplayName"], mappedFields);
    }

    [Fact]
    public async Task Update_Validation_ThrowsValidationException_WithFieldViolations() {
        var request = new Request { DisplayName = "", CreateTime = DateTime.UtcNow };

        var (repository, mapper) = CreateDoubles();
        var validator = new RejectingValidationAdvisor();
        using var services = BuildServices(repository.Object, mapper.Object, services => {
            services.AddSingleton<IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(
                new ResourceUpdateSanitizePipelineAdvisor<Entity, Request, Detail>());
            services.AddSingleton<IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(
                new ResourceUpdateValidationPipelineAdvisor<Entity, Request, Detail>());
            services.AddSingleton<IValidationAdvisor<Request>>(validator);
        });
        var dispatcher = new InProcessRequestDispatcher(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => dispatcher.SendAsync<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
                new("entities/e1", request, null), CancellationToken.None));

        Assert.NotNull(ex.Details);
        var violation = Assert.Single(ex.Details.OfType<BadRequestDetail>().Single().FieldViolations!);
        Assert.Equal(nameof(Request.DisplayName), violation.Field);
        Assert.Null(validator.SeenCreateTime);
    }

    [Fact]
    public async Task Update_SuppressedValidation_SkipsValidator() {
        var request = new Request { DisplayName = "" };

        var (repository, mapper) = CreateDoubles();
        var validator = new RejectingValidationAdvisor();
        using var services = BuildServices(
            repository.Object,
            mapper.Object,
            services => {
                services.AddSingleton<IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(
                    new ResourceUpdateValidationPipelineAdvisor<Entity, Request, Detail>());
                services.AddSingleton<IValidationAdvisor<Request>>(validator);
            },
            new() { SuppressUpdateValidation = true });
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
            new("entities/e1", request, null), CancellationToken.None);

        Assert.Same(MappedDetail, result.Detail);
        Assert.False(validator.Invoked);
    }


    [Fact]
    public async Task Update_Authorization_Sees_Client_System_Fields_Before_Sanitize_And_Handler_Sees_Scrubbed_Payload() {
        var request = new Request {
            Name       = "entities/forged",
            CreateTime = DateTime.UtcNow,
            UpdateMask = "display_name,create_time",
        };
        var (repository, mapper) = CreateDoubles();
        Request? mapped = null;
        mapper.Setup(value => value.Map<Request, Entity>(It.IsAny<Request>(), It.IsAny<Entity>(), It.IsAny<IEnumerable<string>>()))
              .Callback((Request value, Entity _, IEnumerable<string> _) => mapped = value);
        Request? authorized = null;
        string? authorizedName = null;
        DateTime? authorizedCreateTime = null;
        string? authorizedUpdateMask = null;
        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(value => value.Resolve(nameof(Operations.Update), typeof(Entity))).Returns("entities.update");
        var permissionMatcher = new Mock<IPermissionMatcher>();
        permissionMatcher.Setup(value => value.IsMatch(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), "entities.update")).Returns(true);
        var authorization = new AuthorizationPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
            envelope => {
                authorized = envelope.Request;
                authorizedName = envelope.Request.Name;
                authorizedCreateTime = envelope.Request.CreateTime;
                authorizedUpdateMask = envelope.Request.UpdateMask;
                return (nameof(Operations.Update), typeof(Entity));
            }, permissionResolver.Object, permissionMatcher.Object);
        using var services = BuildServices(repository.Object, mapper.Object, service => {
            service.AddSingleton<IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(authorization);
            service.AddSingleton<IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(
                new ResourceUpdateSanitizePipelineAdvisor<Entity, Request, Detail>());
        });
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("test"));
        var dispatcher = new InProcessRequestDispatcher(services);

        await dispatcher.SendAsync<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
            new("entities/e1", request, principal), CancellationToken.None);

        Assert.Same(request, authorized);
        Assert.Equal("entities/forged", authorizedName);
        Assert.NotNull(authorizedCreateTime);
        Assert.Equal("display_name,create_time", authorizedUpdateMask);
        Assert.Null(mapped!.Name);
        Assert.Null(mapped.CreateTime);
        Assert.Equal("display_name", mapped.UpdateMask);
    }
    [Fact]
    public void Wrap_Orders_Follow_SecurityOrders() {
        Assert.Equal(SecurityOrders.Sanitize, new ResourceCreateSanitizePipelineAdvisor<Entity, Request, Detail>().Order);
        Assert.Equal(SecurityOrders.Sanitize, new ResourceUpdateSanitizePipelineAdvisor<Entity, Request, Detail>().Order);
        Assert.Equal(SecurityOrders.Validation, new ResourceCreateValidationPipelineAdvisor<Entity, Request, Detail>().Order);
        Assert.Equal(SecurityOrders.Validation, new ResourceUpdateValidationPipelineAdvisor<Entity, Request, Detail>().Order);
    }

    private static readonly Entity MappedEntity = new() { Name = "e1" };
    private static readonly Detail MappedDetail = new() { Name = "e1" };

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreateDoubles() {
        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.AddAsync(MappedEntity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.UpdateAsync(MappedEntity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<Entity?>(MappedEntity));

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>())).Returns(MappedEntity);
        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>())).Returns(MappedDetail);

        return (repository, mapper);
    }

    private static ServiceProvider BuildServices(
        IRepository<Entity>       repository,
        ISimpleMapper             mapper,
        Action<ServiceCollection> configureAdvisors,
        SchemataResourceOptions?  options = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(mapper);
        services.AddSingleton<ResourceOperationHandler<Entity, Request, Detail, Summary>>();
        services.AddSingleton<
            IRequestHandler<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>,
            DefaultCreateResourceHandler<Entity, Request, Detail, Summary>>();
        services.AddSingleton<
            IRequestHandler<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>,
            DefaultUpdateResourceHandler<Entity, Request, Detail, Summary>>();
        configureAdvisors(services);
        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }

        return services.BuildServiceProvider();
    }

    private sealed class RejectingValidationAdvisor : IValidationAdvisor<Request>
    {
        public int Order => 0;

        public bool      Invoked        { get; private set; }
        public DateTime? SeenCreateTime { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext              ctx,
            Operations                 operation,
            Request                    request,
            IList<ErrorFieldViolation> errors,
            CancellationToken          ct = default
        ) {
            Invoked        = true;
            SeenCreateTime = request.CreateTime;
            errors.Add(new() {
                Field       = nameof(Request.DisplayName),
                Description = "Display name is required.",
            });
            return Task.FromResult(AdviseResult.Block);
        }
    }

    [CanonicalName("entities/{entity}")]
    public sealed class Entity : ICanonicalName
    {
        public string? DisplayName { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName, ITimestamp, ISoftDelete, IUpdateMask, IRequestIdentification
    {
        public string? DisplayName { get; set; }
        public string? RequestId   { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public DateTime? CreateTime { get; set; }
        public DateTime? UpdateTime { get; set; }

        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime  { get; set; }

        public string? UpdateMask { get; set; }
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
