using Schemata.Core.Building;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Handlers;
using Xunit;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation.Commands;
using Schemata.Caching.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Tests.Features;

public class SchemataResourceFeatureRegisterMethodShould
{
    [Fact]
    public void LeaveMethodsEmpty_WhenResourceHasNoResourceMethodAttribute() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<PlainEntity>();

        services.AddResource(resource, registry);

        Assert.Empty(registry.GetMethods(typeof(PlainEntity)));
    }

    [Fact]
    public void StoreBuiltInMethods_WhenResourceIsSoftDeletable() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<SoftEntity>();

        services.AddResource(resource, registry);

        var methods = registry.GetMethods(typeof(SoftEntity)).OrderBy(m => m.Verb).ToArray();

        Assert.Equal(3, methods.Length);
        Assert.Equal("expunge", methods[0].Verb);
        Assert.Equal(typeof(ExpungeHandler<SoftEntity>), methods[0].Handler);
        Assert.Equal(ResourceMethodScope.Instance, methods[0].Scope);
        Assert.Equal("purge", methods[1].Verb);
        Assert.Equal(typeof(PurgeHandler<SoftEntity>), methods[1].Handler);
        Assert.Equal(ResourceMethodScope.Collection, methods[1].Scope);
        Assert.Equal("undelete", methods[2].Verb);
        Assert.Equal(typeof(UndeleteHandler<SoftEntity, SoftEntity>), methods[2].Handler);
        Assert.Equal(ResourceMethodScope.Instance, methods[2].Scope);
    }

    [Fact]
    public void LeaveMethodsEmpty_WhenResourceIsNotSoftDeletable() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<PlainEntity>();

        services.AddResource(resource, registry);

        Assert.Empty(registry.GetMethods(typeof(PlainEntity)));
    }

    [Fact]
    public void PreserveUserDeclaredVerb_WhenSoftDeletableResourceOverridesBuiltIn() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<SoftOverrideEntity, SoftOverrideEntity>();

        services.AddResource(resource, registry);

        var methods = registry.GetMethods(typeof(SoftOverrideEntity)).OrderBy(m => m.Verb).ToArray();

        Assert.Equal(3, methods.Length);
        Assert.Equal("expunge", methods[0].Verb);
        Assert.Equal(typeof(ExpungeHandler<SoftOverrideEntity>), methods[0].Handler);
        Assert.Equal("purge", methods[1].Verb);
        Assert.Equal(typeof(PurgeHandler<SoftOverrideEntity>), methods[1].Handler);
        Assert.Equal(ResourceMethodScope.Collection, methods[1].Scope);
        Assert.Equal("undelete", methods[2].Verb);
        Assert.Equal(typeof(SoftUndeleteHandler), methods[2].Handler);
    }

    [Fact]
    public void HonorOperationsWhitelist_ForPurge() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<SoftEntity> {
            Operations = [Operations.Get, Operations.List, Operations.Undelete, Operations.Expunge],
        };

        services.AddResource(resource, registry);

        var methods = registry.GetMethods(typeof(SoftEntity)).OrderBy(m => m.Verb).ToArray();

        Assert.DoesNotContain(methods, m => m.Verb == "purge");
    }

    [Fact]
    public void PreserveUserDeclaredPurge_WhenSoftDeletableResourceOverridesBuiltIn() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<SoftPurgeOverrideEntity, SoftPurgeOverrideEntity>();

        services.AddResource(resource, registry);

        var method = registry.GetMethods(typeof(SoftPurgeOverrideEntity)).Single(m => m.Verb == "purge");

        Assert.Equal(typeof(SoftPurgeHandler), method.Handler);
        Assert.Equal(ResourceMethodScope.Collection, method.Scope);
    }

    [Fact]
    public void StoreSingleMethod_WhenResourceDeclaresOneVerb() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<SingleVerbEntity, RunRequest>();

        services.AddResource(resource, registry);

        var methods    = registry.GetMethods(typeof(SingleVerbEntity));
        var registered = Assert.Single(methods);
        Assert.Equal("run", registered.Verb);
        Assert.Equal(typeof(RunHandler), registered.Handler);
        Assert.Equal(ResourceMethodScope.Instance, registered.Scope);
    }

    [Fact]
    public void StoreSingleMethod_WhenResourceSuppliesProgrammaticVerb() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<PlainEntity, RunRequest> {
            Methods = [new("run", typeof(PlainRunHandler))],
        };

        services.AddResource(resource, registry);

        var methods    = registry.GetMethods(typeof(PlainEntity));
        var registered = Assert.Single(methods);
        Assert.Equal("run", registered.Verb);
        Assert.Equal(typeof(PlainRunHandler), registered.Handler);
        Assert.Equal(ResourceMethodScope.Instance, registered.Scope);
    }

    [Fact]
    public void StoreSameMethodMetadata_ForAttributeAndProgrammaticRegistration() {
        var attributeRegistry = new ResourceRegistry();
        new ServiceCollection().AddResource(new ResourceAttribute<SingleVerbEntity, RunRequest>(), attributeRegistry);

        var programmaticRegistry = new ResourceRegistry();
        new ServiceCollection().AddResource(new ResourceAttribute<PlainEntity, RunRequest> {
                                                Methods = [new("run", typeof(PlainRunHandler))],
                                            }, programmaticRegistry);

        var attributeMethod = Assert.Single(attributeRegistry.GetMethods(typeof(SingleVerbEntity)));
        var explicitMethod  = Assert.Single(programmaticRegistry.GetMethods(typeof(PlainEntity)));

        Assert.Equal(attributeMethod.Verb, explicitMethod.Verb);
        Assert.Equal(attributeMethod.Scope, explicitMethod.Scope);
        Assert.Equal(ResourceHttpMethod.Post, explicitMethod.Method);
    }

    [Fact]
    public void StoreAllVerbs_WhenResourceDeclaresMultipleMethods() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<MultiVerbEntity, RunRequest>();

        services.AddResource(resource, registry);

        var methods = registry.GetMethods(typeof(MultiVerbEntity)).OrderBy(m => m.Verb).ToArray();

        Assert.Equal(2, methods.Length);
        Assert.Equal("archive", methods[0].Verb);
        Assert.Equal(ResourceMethodScope.Instance, methods[0].Scope);
        Assert.Equal("batchCreate", methods[1].Verb);
        Assert.Equal(ResourceMethodScope.Collection, methods[1].Scope);
    }

    [Fact]
    public void Throw_WhenHandlerDoesNotImplementRequiredInterface() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<InvalidHandlerEntity>();

        var ex
            = Assert.Throws<InvalidOperationException>(() => services.AddResource(resource, registry));

        Assert.Contains("IRequest", ex.Message, StringComparison.Ordinal);
        Assert.Contains("badVerb", ex.Message, StringComparison.Ordinal);
        using var provider = services.BuildServiceProvider();
        Assert.Null(registry.GetResource(typeof(InvalidHandlerEntity)));
        Assert.Null(provider.GetService<IRequestHandler<CreateResourceRequest<InvalidHandlerEntity, InvalidHandlerEntity, InvalidHandlerEntity>, CreateResultBase<InvalidHandlerEntity>>>());
    }

    [Fact]
    public void RegisterOneMethod_WhenTheSameResourceIsDeclaredTwice() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<SingleVerbEntity, RunRequest>(), registry);
        services.AddResource(new ResourceAttribute<SingleVerbEntity, RunRequest>(), registry);

        var registered = Assert.Single(registry.GetMethods(typeof(SingleVerbEntity)));
        Assert.Equal("run", registered.Verb);
    }

    [Fact]
    public void LeaveAnAttributedEntityUnregistered_UntilItIsAddedExplicitly() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<PlainEntity>(), registry);

        Assert.NotNull(registry.GetResource(typeof(PlainEntity)));
        Assert.Null(registry.GetResource(typeof(ScanResource)));
    }

    [Fact]
    public void Share_One_Registry_Across_Builders_Over_The_Same_Options() {
        var schemata = new SchemataOptions();
        var services = new ServiceCollection();
        services.AddSchemataResources(schemata);

        new SchemataResourceBuilder(schemata, services).AddResource<ScanResource>();
        new SchemataResourceBuilder(schemata, services).Use<PlainEntity, PlainEntity, PlainEntity, PlainEntity>();

        using var provider = services.BuildServiceProvider();
        var       registry = provider.GetRequiredService<ResourceRegistry>();

        Assert.NotNull(registry.GetResource(typeof(ScanResource)));
        Assert.NotNull(registry.GetResource(typeof(PlainEntity)));
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task SupplementedOperations_RemoveOnlyOwnedBuiltInMethodClosures() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        var builder = new SchemataResourceBuilder(options, services);
        builder.Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>();
        builder.Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>(null, resource => {
            resource.Methods = [new("run", typeof(PlainRunHandler), ResourceMethodScope.Collection)];
        });
        services.AddSchemataResources(options);
        AddRunDependencies(services);
        builder.Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>(null, resource => resource.Operations = []);
        using var provider = services.BuildServiceProvider();
        var dispatcher = new InProcessRequestDispatcher(provider);
        Assert.Equal("run", Assert.Single(provider.GetRequiredService<ResourceRegistry>().GetMethods(typeof(SoftEntity))).Verb);
        var response = await dispatcher.SendAsync<ResourceMethodRequest<SoftEntity, PlainRunRequest, RunResponse>, RunResponse>(
            new("run", null, new() { Amount = 7 }, null));
        Assert.Equal(12, response.Value);
        Assert.Equal("accounts/one", response.Parent);
        var expunge = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<
            ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>, EmptyResourceResponse>(
                new("expunge", null, new(), null)));
        Assert.Contains(typeof(ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>).FullName!, expunge.Message, StringComparison.Ordinal);
        var purge = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<PurgeResourceRequest<SoftEntity>, Operation>(new()));
        Assert.Contains(typeof(PurgeResourceRequest<SoftEntity>).FullName!, purge.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task ConflictingMethod_DoesNotReplaceTheInstalledConsumerHandler() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder = new SchemataResourceBuilder(options, services);
        builder.Use<PlainEntity, PlainEntity, PlainEntity, PlainEntity>(null, resource => {
            resource.Methods = [new("run", typeof(PlainRunHandler), ResourceMethodScope.Collection)];
        });
        Assert.Throws<InvalidOperationException>(() => builder.Use<PlainEntity, PlainEntity, PlainEntity, PlainEntity>(null,
            resource => resource.Methods = [new("run", typeof(RunHandler), ResourceMethodScope.Collection)]));
        AddRunDependencies(services);
        using var provider = services.BuildServiceProvider();
        var response = await new InProcessRequestDispatcher(provider).SendAsync<
            ResourceMethodRequest<PlainEntity, PlainRunRequest, RunResponse>, RunResponse>(
                new("run", null, new() { Amount = 9 }, null));
        Assert.Equal(14, response.Value);
        Assert.Equal("accounts/one", response.Parent);
        Assert.Equal(typeof(PlainRunHandler), Assert.Single(provider.GetRequiredService<ResourceRegistry>().GetMethods(typeof(PlainEntity))).Handler);
        Assert.Null(provider.GetService<IRequestHandler<RunRequest, RunResponse>>());
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public void LateBuiltInOverride_RejectsBeforeChangingTheInstalledMethod() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder = new SchemataResourceBuilder(options, services);
        builder.Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>();
        Assert.Throws<InvalidOperationException>(() => builder.Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>(null,
            resource => resource.Methods = [new("purge", typeof(SoftPurgeHandler), ResourceMethodScope.Collection)]));
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IRequestHandler<SoftPurgeOverrideRequest, SoftPurgeResponse>>());
        Assert.Equal(typeof(PurgeHandler<SoftEntity>), provider.GetRequiredService<ResourceRegistry>()
            .GetMethods(typeof(SoftEntity)).Single(method => method.Verb == "purge").Handler);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task SupplementedOperations_PreserveDerivedHandlerSharedByAnotherResource() {
        var host = WebApplication.CreateBuilder();
        SchemataOptions? options = null;
        host.UseSchemata(schema => {
            options = schema.Options;
            var resource = schema.UseResource();
            resource.Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>();
            resource.Use<PlainEntity, PlainEntity, PlainEntity, PlainEntity>(null, descriptor => {
                descriptor.Methods = [new("removeArchived", typeof(ExpungeHandler<SoftEntity>), ResourceMethodScope.Collection)];
            });
        });
        var services = host.Services;
        var archived = new SoftEntity { Name = "archived", CanonicalName = "softEntities/archived", DeleteTime = new DateTime(2026, 1, 1) };
        var repository = new Mock<IRepository<SoftEntity>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<SoftEntity>, IQueryable<SoftEntity>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<SoftEntity>, IQueryable<SoftEntity>> query, CancellationToken _) =>
                new ValueTask<SoftEntity?>(query(new[] { archived }.AsQueryable()).SingleOrDefault()));
        var mutation = new Mock<IResourceMutation<SoftEntity>>();
        mutation.Setup(m => m.DeleteAsync(archived, null, Operations.Expunge, It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        services.AddSingleton(repository.Object);
        services.AddSingleton(mutation.Object);
        services.AddSingleton(Mock.Of<IRepository<PlainEntity>>());
        services.AddSingleton(Mock.Of<ICacheProvider>());
        new SchemataResourceBuilder(options!, services).Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>(null,
            resource => resource.Operations = []);
        await using var app = host.Build();
        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var registry = provider.GetRequiredService<ResourceRegistry>();
        Assert.Empty(registry.GetMethods(typeof(SoftEntity)));
        Assert.Equal("removeArchived", Assert.Single(registry.GetMethods(typeof(PlainEntity))).Verb);
        var dispatcher = new InProcessRequestDispatcher(provider);
        await dispatcher.SendAsync<ResourceMethodRequest<PlainEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>, EmptyResourceResponse>(
            new("removeArchived", null, new() { CanonicalName = "softEntities/archived" }, null));
        mutation.Verify(m => m.DeleteAsync(archived, null, Operations.Expunge, It.IsAny<CancellationToken>()), Times.Once);
        var expunge = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<
            ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>, EmptyResourceResponse>(
                new("expunge", null, new(), null)));
        Assert.Contains(typeof(ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>).FullName!, expunge.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task SupplementedOperations_AfterPublicHostFlush_PruneOwnedMethodsAndPreserveApplicationHandler() {
        var host = WebApplication.CreateBuilder();
        SchemataOptions? options = null;
        host.UseSchemata(schema => {
            options = schema.Options;
            schema.UseResource().Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>(null, resource => {
                resource.Methods = [new("run", typeof(PlainRunHandler), ResourceMethodScope.Collection)];
            });
            AddRunDependencies(schema.Services);
        });
        var archived = new SoftEntity { CanonicalName = "softEntities/application", DeleteTime = new DateTime(2026, 1, 1) };
        var repository = new Mock<IRepository<SoftEntity>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<SoftEntity>, IQueryable<SoftEntity>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<SoftEntity>, IQueryable<SoftEntity>> query, CancellationToken _) =>
                new ValueTask<SoftEntity?>(query(new[] { archived }.AsQueryable()).SingleOrDefault()));
        var mutation = new Mock<IResourceMutation<SoftEntity>>();
        mutation.Setup(m => m.DeleteAsync(archived, null, Operations.Expunge, It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        var application = new ExpungeHandler<SoftEntity>(repository.Object, mutation.Object);
        host.Services.AddSingleton<IRequestHandler<ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>>(application);
        new SchemataResourceBuilder(options!, host.Services).Use<SoftEntity, SoftEntity, SoftEntity, SoftEntity>(null,
            resource => resource.Operations = []);
        await using var app = host.Build();
        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var dispatcher = new InProcessRequestDispatcher(provider);
        Assert.Equal("run", Assert.Single(provider.GetRequiredService<ResourceRegistry>().GetMethods(typeof(SoftEntity))).Verb);
        var response = await dispatcher.SendAsync<ResourceMethodRequest<SoftEntity, PlainRunRequest, RunResponse>, RunResponse>(
            new("run", null, new() { Amount = 11 }, null));
        Assert.Equal(16, response.Value);
        Assert.Equal("accounts/one", response.Parent);
        var expunge = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<
            ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>, EmptyResourceResponse>(
                new("expunge", null, new(), null)));
        Assert.Contains(typeof(ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>).FullName!, expunge.Message, StringComparison.Ordinal);
        var purge = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<PurgeResourceRequest<SoftEntity>, Operation>(new()));
        Assert.Contains(typeof(PurgeResourceRequest<SoftEntity>).FullName!, purge.Message, StringComparison.Ordinal);
        var undelete = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<UndeleteResourceRequest<SoftEntity, SoftEntity>, SoftEntity>(new()));
        Assert.Contains(typeof(UndeleteResourceRequest<SoftEntity, SoftEntity>).FullName!, undelete.Message, StringComparison.Ordinal);
        Assert.Empty(provider.GetServices<IRequestPipelineAdvisor<ResourceMethodRequest<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>, EmptyResourceResponse>>());
        Assert.Empty(provider.GetServices<IResourceMethodAdvisor<SoftEntity, ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>>());
        await dispatcher.SendAsync<ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>(new() { CanonicalName = "softEntities/application" });
        mutation.Verify(m => m.DeleteAsync(archived, null, Operations.Expunge, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static void AddRunDependencies(IServiceCollection services) {
        var repository = new Mock<IRepository<PlainEntity>>();
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<PlainEntity>, IQueryable<PlainEntity>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PlainEntity?>(new PlainEntity { Value = 5 }));
        services.AddSingleton(repository.Object);
        services.AddSingleton(Mock.Of<IRepository<SoftEntity>>());
        services.AddSingleton(Mock.Of<ICacheProvider>());
    }


    #region Nested type: InvalidHandlerEntity

    [ResourceMethod("badVerb", typeof(NotAHandler))]
    [CanonicalName("invalidHandlerEntities/{invalid_handler_entity}")]
    public sealed class InvalidHandlerEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: MultiVerbEntity

    [ResourceMethod("archive", typeof(RunHandler))]
    [ResourceMethod("batchCreate", typeof(RunHandler), ResourceMethodScope.Collection)]
    [CanonicalName("multiVerbEntities/{multi_verb_entity}")]
    public sealed class MultiVerbEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: NotAHandler

    public sealed class NotAHandler;

    #endregion

    #region Nested type: PlainEntity

    [CanonicalName("plainEntities/{plain_entity}")]
    public sealed class PlainEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public int Value { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: PlainRunHandler

    public sealed class PlainRunHandler(IRepository<PlainEntity> repository) : IRequestHandler<PlainRunRequest, RunResponse>
    {
        #region IRequestHandler<PlainRunRequest,RunResponse> Members

        public async Task<RunResponse> HandleAsync(
            PlainRunRequest request,
            CancellationToken ct = default
        ) {
            var entity = await repository.SingleOrDefaultAsync(q => q, ct);
            return new RunResponse { Value = entity!.Value + request.Amount, CanonicalName = "accounts/one/results/total" };
        }

        #endregion
    }

    #endregion

    #region Nested type: PlainRunRequest

    public sealed class PlainRunRequest : IRequest<RunResponse>, IRequestPrincipal, ICanonicalName
    {
        public string?          Name          { get; set; }
        public string?          CanonicalName { get; set; }
        public ClaimsPrincipal? Principal     { get; set; }
        public int Amount { get; set; }
    }

    #endregion

    #region Nested type: RunHandler

    public sealed class RunHandler : IRequestHandler<RunRequest, RunResponse>
    {
        #region IRequestHandler<RunRequest,RunResponse> Members

        public Task<RunResponse> HandleAsync(
            RunRequest        request,
            CancellationToken ct = default
        ) {
            return Task.FromResult(new RunResponse());
        }

        #endregion
    }

    #endregion

    #region Nested type: RunRequest

    public sealed class RunRequest : IRequest<RunResponse>, IRequestPrincipal, ICanonicalName
    {
        public string?          Name          { get; set; }
        public string?          CanonicalName { get; set; }
        public ClaimsPrincipal? Principal     { get; set; }
    }

    #endregion

    #region Nested type: RunResponse

    public sealed class RunResponse : ICanonicalName, IChild
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public int Value { get; set; }
        public string? Parent { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: ScanResource

    [Resource<ScanResource>]
    [CanonicalName("scanResources/{scan_resource}")]
    public sealed class ScanResource : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: SingleVerbEntity

    [ResourceMethod("run", typeof(RunHandler))]
    [CanonicalName("singleVerbEntities/{single_verb_entity}")]
    public sealed class SingleVerbEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: SoftEntity

    [CanonicalName("softEntities/{soft_entity}")]
    public sealed class SoftEntity : ICanonicalName, ISoftDelete
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion

        #region ISoftDelete Members

        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime  { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: SoftOverrideEntity

    [ResourceMethod("undelete", typeof(SoftUndeleteHandler))]
    [CanonicalName("softOverrideEntities/{soft_override_entity}")]
    public sealed class SoftOverrideEntity : ICanonicalName, ISoftDelete
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion

        #region ISoftDelete Members

        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime  { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: SoftPurgeHandler

    public sealed class SoftPurgeHandler : IRequestHandler<SoftPurgeOverrideRequest, SoftPurgeResponse>
    {
        #region IRequestHandler<SoftPurgeOverrideRequest,SoftPurgeResponse> Members

        public Task<SoftPurgeResponse> HandleAsync(
            SoftPurgeOverrideRequest request,
            CancellationToken        ct = default
        ) {
            return Task.FromResult(new SoftPurgeResponse());
        }

        #endregion
    }

    #endregion

    #region Nested type: SoftPurgeOverrideEntity

    [ResourceMethod("purge", typeof(SoftPurgeHandler), ResourceMethodScope.Collection)]
    [CanonicalName("softPurgeOverrideEntities/{soft_purge_override_entity}")]
    public sealed class SoftPurgeOverrideEntity : ICanonicalName, ISoftDelete
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion

        #region ISoftDelete Members

        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime  { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: SoftPurgeOverrideRequest

    public sealed class SoftPurgeOverrideRequest : IRequest<SoftPurgeResponse>, IRequestPrincipal, ICanonicalName
    {
        public string?          Name          { get; set; }
        public string?          CanonicalName { get; set; }
        public ClaimsPrincipal? Principal     { get; set; }
        public string?          Filter        { get; set; }
        public bool             Force         { get; set; }
    }

    #endregion

    #region Nested type: SoftPurgeResponse

    public sealed class SoftPurgeResponse : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion

    #region Nested type: SoftUndeleteHandler

    public sealed class SoftUndeleteHandler : IRequestHandler<SoftUndeleteOverrideRequest, SoftOverrideEntity>
    {
        #region IRequestHandler<SoftUndeleteOverrideRequest,SoftOverrideEntity> Members

        public Task<SoftOverrideEntity> HandleAsync(
            SoftUndeleteOverrideRequest request,
            CancellationToken           ct = default
        ) {
            return Task.FromResult(new SoftOverrideEntity());
        }

        #endregion
    }

    #endregion

    #region Nested type: SoftUndeleteOverrideRequest

    public sealed class SoftUndeleteOverrideRequest : IRequest<SoftOverrideEntity>, IRequestPrincipal, ICanonicalName
    {
        public string?          Name          { get; set; }
        public string?          CanonicalName { get; set; }
        public ClaimsPrincipal? Principal     { get; set; }
    }

    #endregion
}
