using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

[Trait("Layer", "Integration")]
public class ResourceInstallationReplayShould
{
    private const string Name = "replayWidgets/one";
    private const string StaleTag = "W/\"stale\"";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task WithoutFreshness_PublicHostReplay_AllowsStaleUpdateAndDeleteWithoutResponseTag(bool withoutFirst, bool repeatInstall) {
        await using var app = BuildHost(withoutFirst, repeatInstall);
        using var scope = app.Services.CreateScope();
        var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);

        var result = await dispatcher.SendAsync<UpdateResourceRequest<Widget, Widget, Widget>, UpdateResultBase<Widget>>(
            new(Name, new() { EntityTag = StaleTag }, null));
        Assert.NotNull(result.Detail);
        Assert.Null(result.Detail.EntityTag);

        await dispatcher.SendAsync<DeleteResourceRequest<Widget, Widget>, DeleteResultBase<Widget>>(
            new(Name, StaleTag, null));
        var mutation = Mock.Get(scope.ServiceProvider.GetRequiredService<IResourceMutation<Widget>>());
        mutation.Verify(m => m.UpdateAsync(It.IsAny<Widget>(), null, Operations.Update, It.IsAny<CancellationToken>()), Times.Once);
        mutation.Verify(m => m.DeleteAsync(It.IsAny<Widget>(), null, Operations.Delete, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithoutFreshness_CustomMethodRegistrationOrder_AllowsStaleTag(bool withoutFirst) {
        await using var app = BuildHost(withoutFirst);
        using var scope = app.Services.CreateScope();
        var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);

        var result = await dispatcher.SendAsync<ResourceMethodRequest<Widget, InspectRequest, Widget>, Widget>(
            new("inspect", Name, new() { EntityTag = StaleTag }, null));

        Assert.Equal(Name, result.CanonicalName);
        Assert.Null(result.EntityTag);
    }

    [Fact]
    public async Task FreshnessInstalled_PublicHostReplay_RejectsStaleCustomMethod() {
        await using var app = BuildHost(disableFreshness: false);
        using var scope = app.Services.CreateScope();
        var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);

        await Assert.ThrowsAsync<AbortedException>(() => dispatcher.SendAsync<ResourceMethodRequest<Widget, InspectRequest, Widget>, Widget>(
            new("inspect", Name, new() { EntityTag = StaleTag }, null)));
    }

    [Fact]
    public async Task FreshnessInstalled_PublicHostReplay_EmitsTagAndRejectsStaleStandardWrites() {
        await using var app = BuildHost(disableFreshness: false);
        using var scope = app.Services.CreateScope();
        var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);
        var result = await dispatcher.SendAsync<GetResourceQueryRequest<Widget, Widget>, GetResultBase<Widget>>(
            new(new() { CanonicalName = Name }, null));
        Assert.NotNull(result.Detail);
        Assert.Equal("W/\"suMxj6KJyUSSJw7pws0CTw\"", result.Detail.EntityTag);

        await Assert.ThrowsAsync<AbortedException>(() => dispatcher.SendAsync<UpdateResourceRequest<Widget, Widget, Widget>, UpdateResultBase<Widget>>(
            new(Name, new() { EntityTag = StaleTag }, null)));
        await Assert.ThrowsAsync<AbortedException>(() => dispatcher.SendAsync<DeleteResourceRequest<Widget, Widget>, DeleteResultBase<Widget>>(
            new(Name, StaleTag, null)));
        var mutation = Mock.Get(scope.ServiceProvider.GetRequiredService<IResourceMutation<Widget>>());
        mutation.Verify(m => m.UpdateAsync(It.IsAny<Widget>(), null, Operations.Update, It.IsAny<CancellationToken>()), Times.Never);
        mutation.Verify(m => m.DeleteAsync(It.IsAny<Widget>(), null, Operations.Delete, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutFreshness_PublicHostReplay_PreservesCustomEntityTagProvider() {
        await using var app = BuildHost(customProvider: true);
        using var scope = app.Services.CreateScope();
        var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);

        var result = await dispatcher.SendAsync<GetResourceQueryRequest<Widget, Widget>, GetResultBase<Widget>>(
            new(new() { CanonicalName = Name }, null));

        Assert.NotNull(result.Detail);
        Assert.Equal("W/\"domain\"", result.Detail.EntityTag);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("freshness")]
    [InlineData("all")]
    public async Task DeferredExclusion_WithoutResources_PublicHostBootsWithEmptyRegistry(string exclusion) {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            var deferred = new SchemataResourceBuilder(schema.Options, schema.Services);
            if (exclusion is "create" or "all") deferred.WithoutCreateValidation();
            if (exclusion is "update" or "all") deferred.WithoutUpdateValidation();
            if (exclusion is "freshness" or "all") deferred.WithoutFreshness();
            schema.UseResource();
        });
        await using var empty = builder.Build();
        Assert.Empty(empty.Services.GetRequiredService<ResourceRegistry>().Resources);
    }

    [Fact]
    public async Task DeferredExclusions_PublicHostDispatchesLaterResourceWithoutFreshness() {
        await using var populated = BuildHost(deferred: true);
        using var scope = populated.Services.CreateScope();
        var dispatcher = new InProcessRequestDispatcher(scope.ServiceProvider);
        var result = await dispatcher.SendAsync<ResourceMethodRequest<Widget, InspectRequest, Widget>, Widget>(
            new("inspect", Name, new() { EntityTag = StaleTag }, null));
        Assert.Equal(Name, result.CanonicalName);
        Assert.Null(result.EntityTag);
    }

    private static WebApplication BuildHost(bool withoutFirst = true, bool repeatInstall = false, bool disableFreshness = true, bool customProvider = false, bool deferred = false) {
        var builder = WebApplication.CreateBuilder();
        SchemataBuilder? configured = null;
        var entity = new Widget { Name = "one", CanonicalName = Name, Timestamp = Guid.Parse("8f31e3b2-89a2-44c9-9227-0ee9c2cd024f") };
        var repository = new Mock<IRepository<Widget>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<Widget>, IQueryable<Widget>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Widget?>(entity));
        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Widget, Widget>(It.IsAny<Widget>())).Returns(() => new Widget {
            Name = entity.Name, CanonicalName = entity.CanonicalName, Timestamp = entity.Timestamp,
        });
        builder.UseSchemata(schema => {
            configured = schema;
            if (deferred) {
                new SchemataResourceBuilder(schema.Options, schema.Services)
                    .WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();
            }
            var resource = schema.UseResource();
            if (disableFreshness && withoutFirst) resource.WithoutFreshness();
            resource.Use<Widget, Widget, Widget, Widget>(null, descriptor => descriptor.Methods = [new("inspect", typeof(InspectHandler))]);
            if (disableFreshness && !withoutFirst) resource.WithoutFreshness();
            if (repeatInstall) schema.UseResource();
            schema.Services.AddSingleton(repository.Object);
            schema.Services.AddSingleton(mapper.Object);
            schema.Services.AddSingleton(Mock.Of<ICacheProvider>());
            schema.Services.AddSingleton(ResourceMutationMock.Create<Widget>().Object);
            if (customProvider) {
                schema.Services.AddSingleton<IEntityTagProvider, DomainEntityTags>();
                schema.Services.AddKeyedSingleton<IEntityTagProvider, DomainEntityTags>("domain");
            }
        });
        if (repeatInstall) configured!.Invoke(builder.Services);
        return builder.Build();
    }

    [CanonicalName("replayWidgets/{widget}")]
    public sealed class Widget : ICanonicalName, IConcurrency, IFreshness
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public Guid Timestamp { get; set; }
        public string? EntityTag { get; set; }
    }

    public sealed class InspectRequest : ICanonicalName, IFreshness, ICommand<Widget>, IRequestPrincipal
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public string? EntityTag { get; set; }
        public ClaimsPrincipal? Principal { get; set; }
    }

    public sealed class InspectHandler(IRepository<Widget> repository) : IRequestHandler<InspectRequest, Widget>
    {
        public async Task<Widget> HandleAsync(InspectRequest request, CancellationToken ct = default) {
            return (await repository.SingleOrDefaultAsync(q => q.Where(w => w.CanonicalName == request.CanonicalName), ct))!;
        }
    }

    public sealed class DomainEntityTags : IEntityTagProvider
    {
        public string? GetEntityTag<TEntity, TDetail>(TDetail? detail, AdviceContext ctx)
            where TEntity : class, ICanonicalName
            where TDetail : class, ICanonicalName => "W/\"domain\"";
    }
}
