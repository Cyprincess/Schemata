using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Core;
using Schemata.Common;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Core.Building;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests;

public sealed class ResourceAuthorizationRegistrationShould
{
    [Fact]
    public void Authorization_Before_Resource_Registration_Closes_Later_Resource_Envelopes() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder  = new SchemataResourceBuilder(options, services);

        builder.WithAuthorization();
        builder.Use<Entity, Request, Detail, Summary>();

        var request  = typeof(CreateResourceRequest<Entity, Request, Detail>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(request, typeof(CreateResultBase<Detail>));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.DoesNotContain(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(request, typeof(CreateResultBase<Detail>)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(request, typeof(CreateResultBase<Detail>)), advisors);
    }

    [Fact]
    public void Combined_Activation_Closes_Later_Resource_Envelopes() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder  = new SchemataResourceBuilder(options, services);

        builder.WithAuthentication().WithAuthorization();
        builder.Use<Entity, Request, Detail, Summary>();

        var request  = typeof(CreateResourceRequest<Entity, Request, Detail>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(request, typeof(CreateResultBase<Detail>));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.Contains(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(request, typeof(CreateResultBase<Detail>)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(request, typeof(CreateResultBase<Detail>)), advisors);
    }

    [Fact]
    public void Authorization_Registers_And_Resolves_All_Standard_And_Method_Closures() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder = new SchemataResourceBuilder(options, services);
        builder.WithAuthorization();
        builder.Use<Entity, Request, Detail, Summary>(null, resource => resource.Methods = [new("archive", typeof(MethodHandler))]);

        VerifyAuthorization<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(services, new(new(), null), nameof(Operations.Create));
        VerifyAuthorization<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(services, new("entities/e1", new(), null), nameof(Operations.Update));
        VerifyAuthorization<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>(services, new(new(), null), nameof(Operations.Get));
        VerifyAuthorization<ListResourceQueryRequest<Entity, Summary>, ListResultBase<Summary>>(services, new(new(), null), nameof(Operations.List));
        VerifyAuthorization<DeleteResourceRequest<Entity, Detail>, DeleteResultBase<Detail>>(services, new("entities/e1", null, null), nameof(Operations.Delete));
        VerifyAuthorization<ResourceMethodRequest<Entity, MethodRequest, MethodResponse>, MethodResponse>(services, new("archive", "entities/e1", new(), null), "archive");
    }

    [Fact]
    public void Resource_Before_Security_Activation_Closes_Registered_Envelopes() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder = new SchemataResourceBuilder(options, services);
        builder.Use<Entity, Request, Detail, Summary>();
        builder.WithAuthentication().WithAuthorization();

        var service = typeof(IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == service).ToArray();

        Assert.Single(descriptors, descriptor => descriptor.ImplementationType == typeof(AuthenticationPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>));
        Assert.Single(descriptors, descriptor => descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>));
    }

    [Fact]
    public void Repeated_Security_Activation_Does_Not_Duplicate_Resource_Advisors_Or_Resolvers() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder = new SchemataResourceBuilder(options, services);
        builder.WithAuthentication().WithAuthentication().WithAuthorization().WithAuthorization();
        builder.Use<Entity, Request, Detail, Summary>();

        var service = typeof(IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == service).ToArray();

        Assert.Single(descriptors, descriptor => descriptor.ImplementationType == typeof(AuthenticationPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>));
        Assert.Single(descriptors, descriptor => descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>));
        var resolverType = typeof(Func<CreateResourceRequest<Entity, Request, Detail>, (string Operation, Type? Entity)>);
        Assert.Equal(1, services.Count(descriptor => descriptor.ServiceType == resolverType));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<CreateResourceRequest<Entity, Request, Detail>, (string Operation, Type? Entity)>>();
        var actual = resolve(new(new(), null));
        Assert.Equal(nameof(Operations.Create), actual.Operation);
        Assert.Equal(typeof(Entity), actual.Entity);
    }
    [Fact]
    public void Authorization_Before_Method_Registration_Closes_The_Custom_Method_Envelope() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var builder = new SchemataResourceBuilder(options, services);
        builder.WithAuthorization();
        builder.Use<Entity, Request, Detail, Summary>(null, resource => resource.Methods = [new("archive", typeof(MethodHandler))]);
        var envelope = typeof(ResourceMethodRequest<Entity, MethodRequest, MethodResponse>);
        var service = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(MethodResponse));
        var resolverType = typeof(Func<ResourceMethodRequest<Entity, MethodRequest, MethodResponse>, (string Operation, Type? Entity)>);

        Assert.Single(services, descriptor => descriptor.ServiceType == service
                                           && descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<ResourceMethodRequest<Entity, MethodRequest, MethodResponse>, MethodResponse>));
        Assert.Equal(1, services.Count(descriptor => descriptor.ServiceType == resolverType));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<ResourceMethodRequest<Entity, MethodRequest, MethodResponse>, (string Operation, Type? Entity)>>();

        var actual = resolve(new("archive", "entities/e1", new() { Principal = new(new ClaimsIdentity("test")) }, new(new ClaimsIdentity("test"))));

        Assert.Equal("archive", actual.Operation);
        Assert.Equal(typeof(Entity), actual.Entity);
    }


    [Fact]
    public void First_Resolver_Registration_Wins_For_A_Closed_Resource_Envelope() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        services.AddScoped<Func<CreateResourceRequest<Entity, Request, Detail>, (string Operation, Type? Entity)>>(_ => _ => ("first", typeof(AnonymousEntity)));
        var builder = new SchemataResourceBuilder(options, services);
        builder.WithAuthorization();
        builder.Use<Entity, Request, Detail, Summary>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<CreateResourceRequest<Entity, Request, Detail>, (string Operation, Type? Entity)>>();

        var actual = resolve(new(new(), null));

        Assert.Equal("first", actual.Operation);
        Assert.Equal(typeof(AnonymousEntity), actual.Entity);
    }

    private static void VerifyAuthorization<TRequest, TResponse>(ServiceCollection services, TRequest request, string operation)
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class {
        var service = typeof(IRequestPipelineAdvisor<TRequest, TResponse>);
        var descriptor = Assert.Single(services, value => value.ServiceType == service && value.ImplementationType == typeof(AuthorizationPipelineAdvisor<TRequest, TResponse>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<TRequest, (string Operation, Type? Entity)>>();

        var actual = resolve(request);

        Assert.Equal(operation, actual.Operation);
        Assert.Equal(typeof(Entity), actual.Entity);
    }

    [Fact]
    public async Task Access_Advisors_Pass_The_Expected_Loaded_Entity_Request_Operation_And_Principal() {
        var entity    = new Entity { Name = "e1", CanonicalName = "entities/e1" };
        var request   = new Request { Name = "request" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var access    = new Mock<IAccessProvider<Entity, Request>>(MockBehavior.Strict);
        access.Setup(provider => provider.HasAccessAsync(null,
                         It.Is<AccessContext<Request>>(context => context.Operation == nameof(Operations.Create) && ReferenceEquals(context.Request, request)),
                         principal, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        access.Setup(provider => provider.HasAccessAsync(entity,
                         It.Is<AccessContext<Request>>(context => context.Operation == nameof(Operations.Update) && ReferenceEquals(context.Request, request)),
                         principal, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        var create = new ResourceCreateAccessAdvisor<Entity, Request>(access.Object);
        var update = new ResourceUpdateAccessAdvisor<Entity, Request>(access.Object);

        var createResult = await create.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, entity, principal);
        var updateResult = await update.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, entity, principal);

        Assert.Equal(AdviseResult.Continue, createResult);
        Assert.Equal(AdviseResult.Continue, updateResult);
        access.VerifyAll();
    }

    [Fact]
    public async Task Access_Advisors_Pass_Loaded_Entities_For_Get_And_Delete() {
        var entity    = new Entity { Name = "e1", CanonicalName = "entities/e1" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var get       = new GetRequest { Name = entity.CanonicalName };
        var delete    = new DeleteRequest { Name = entity.CanonicalName };
        var getAccess = new Mock<IAccessProvider<Entity, GetRequest>>(MockBehavior.Strict);
        var deleteAccess = new Mock<IAccessProvider<Entity, DeleteRequest>>(MockBehavior.Strict);
        getAccess.Setup(provider => provider.HasAccessAsync(entity,
                            It.Is<AccessContext<GetRequest>>(context => context.Operation == nameof(Operations.Get) && ReferenceEquals(context.Request, get)),
                            principal, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        deleteAccess.Setup(provider => provider.HasAccessAsync(entity,
                               It.Is<AccessContext<DeleteRequest>>(context => context.Operation == nameof(Operations.Delete) && ReferenceEquals(context.Request, delete)),
                               principal, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);

        var getResult = await new ResourceGetAccessAdvisor<Entity>(getAccess.Object).AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), get, entity, principal);
        var deleteResult = await new ResourceDeleteAccessAdvisor<Entity>(deleteAccess.Object).AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), delete, entity, principal);

        Assert.Equal(AdviseResult.Continue, getResult);
        Assert.Equal(AdviseResult.Continue, deleteResult);
        getAccess.VerifyAll();
        deleteAccess.VerifyAll();
    }

    [Fact]
    public async Task List_Access_Has_No_Loaded_Entity_And_Entitlement_Filters_The_Container() {
        var request   = new ListRequest { Parent = "parents/one" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var access    = new Mock<IAccessProvider<Entity, ListRequest>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<Entity, ListRequest>>(MockBehavior.Strict);
        access.Setup(provider => provider.HasAccessAsync(null,
                         It.Is<AccessContext<ListRequest>>(context => context.Operation == nameof(Operations.List) && ReferenceEquals(context.Request, request)),
                         principal, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        entitlement.Setup(provider => provider.GenerateEntitlementExpressionAsync(
                               It.Is<AccessContext<ListRequest>>(context => context.Operation == nameof(Operations.List) && ReferenceEquals(context.Request, request)),
                               principal, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(entity => entity.Name == "allowed");
        var container = new ResourceRequestContainer<Entity>();

        var accessResult = await new ResourceListAccessAdvisor<Entity>(access.Object).AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, container, principal);
        var entitlementResult = await new ResourceEntitlementListAdvisor<Entity>(entitlement.Object).AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, container, principal);
        var names = container.Query(new[] { new Entity { Name = "allowed" }, new Entity { Name = "denied" } }.AsQueryable()).Select(entity => entity.Name).ToArray();

        Assert.Equal(AdviseResult.Continue, accessResult);
        Assert.Equal(AdviseResult.Continue, entitlementResult);
        Assert.Equal(new[] { "allowed" }, names);
        access.VerifyAll();
        entitlement.VerifyAll();
    }

    [Fact]
    public async Task Method_Access_Uses_The_Context_Verb_And_Loaded_Entity() {
        var entity    = new Entity { Name = "e1", CanonicalName = "entities/e1" };
        var request   = new MethodRequest();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var access    = new Mock<IAccessProvider<Entity, MethodRequest>>(MockBehavior.Strict);
        access.Setup(provider => provider.HasAccessAsync(entity,
                         It.Is<AccessContext<MethodRequest>>(context => context.Operation == "archive" && ReferenceEquals(context.Request, request)),
                         principal, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        var context = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        context.Set(new ResourceMethodVerb("archive"));

        var result = await new ResourceMethodAccessAdvisor<Entity, MethodRequest, MethodResponse>(access.Object).AdviseAsync(context, request, entity, principal);

        Assert.Equal(AdviseResult.Continue, result);
        access.VerifyAll();
    }
    [Fact]
    public async Task Create_Access_Denial_Uses_Null_Entity_And_Reports_Parent_Visible_Permission_Denial() {
        var request = new Request { Name = "created" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var access = new Mock<IAccessProvider<Entity, Request>>(MockBehavior.Strict);
        access.SetupSequence(provider => provider.HasAccessAsync(null, It.IsAny<AccessContext<Request>>(), principal, It.IsAny<CancellationToken>()))
              .ReturnsAsync(false)
              .ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() => new ResourceCreateAccessAdvisor<Entity, Request>(access.Object).AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, new(), principal));

        Assert.Equal(403, exception.Code);
        Assert.Equal("PERMISSION_DENIED", exception.Status);
        Assert.Equal(ErrorReasons.InsufficientPermission, Assert.Single(exception.Details!.OfType<ErrorInfoDetail>()).Reason);
        access.Verify(provider => provider.HasAccessAsync(null, It.Is<AccessContext<Request>>(context => context.Operation == nameof(Operations.Create) && ReferenceEquals(context.Request, request)), principal, It.IsAny<CancellationToken>()), Times.Once);
        access.Verify(provider => provider.HasAccessAsync(null, It.Is<AccessContext<Request>>(context => context.Operation == nameof(Operations.Get) && ReferenceEquals(context.Request, request)), principal, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Access_Denial_Hides_Loaded_Entity_When_Parent_Read_Fails() {
        var entity = new Entity { Name = "e1", CanonicalName = "entities/e1" };
        var request = new Request { Name = "e1" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var access = new Mock<IAccessProvider<Entity, Request>>(MockBehavior.Strict);
        access.SetupSequence(provider => provider.HasAccessAsync(entity, It.IsAny<AccessContext<Request>>(), principal, It.IsAny<CancellationToken>()))
              .ReturnsAsync(false)
              .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => new ResourceUpdateAccessAdvisor<Entity, Request>(access.Object).AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, entity, principal));

        Assert.Equal(404, exception.Code);
        Assert.Equal("NOT_FOUND", exception.Status);
        Assert.Equal(ErrorReasons.ResourceNotFound, Assert.Single(exception.Details!.OfType<ErrorInfoDetail>()).Reason);
        access.Verify(provider => provider.HasAccessAsync(entity, It.Is<AccessContext<Request>>(context => context.Operation == nameof(Operations.Update) && ReferenceEquals(context.Request, request)), principal, It.IsAny<CancellationToken>()), Times.Once);
        access.Verify(provider => provider.HasAccessAsync(entity, It.Is<AccessContext<Request>>(context => context.Operation == nameof(Operations.Get) && ReferenceEquals(context.Request, request)), principal, It.IsAny<CancellationToken>()), Times.Once);
    }


    [Fact]
    public async Task Anonymous_List_Bypasses_Wrap_And_Instance_Access_But_Applies_Entitlement() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        services.AddSchemataResources(options);
        var request = new ListRequest();
        var access = new Mock<IAccessProvider<AnonymousEntity, ListRequest>>(MockBehavior.Strict);
        var entitlement = new Mock<IEntitlementProvider<AnonymousEntity, ListRequest>>(MockBehavior.Strict);
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var matcher = new Mock<IPermissionMatcher>(MockBehavior.Strict);
        entitlement.Setup(provider => provider.GenerateEntitlementExpressionAsync(
                               It.Is<AccessContext<ListRequest>>(context => context.Operation == nameof(Operations.List) && ReferenceEquals(context.Request, request)),
                               null, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(entity => entity.Name == "allowed");
        services.AddSingleton(access.Object);
        services.AddSingleton(entitlement.Object);
        services.AddSingleton(resolver.Object);
        services.AddSingleton(matcher.Object);
        var builder = new SchemataResourceBuilder(options, services);
        builder.WithAuthentication().WithAuthorization();
        builder.Use<AnonymousEntity, Request, Detail, Summary>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var envelope = new ListResourceQueryRequest<AnonymousEntity, Summary>(request, null);
        var context = new AdviceContext(scope.ServiceProvider);
        var advisors = scope.ServiceProvider.GetServices<IRequestPipelineAdvisor<ListResourceQueryRequest<AnonymousEntity, Summary>, ListResultBase<Summary>>>().ToArray();
        var authentication = Assert.Single(advisors.OfType<AuthenticationPipelineAdvisor<ListResourceQueryRequest<AnonymousEntity, Summary>, ListResultBase<Summary>>>());
        var authorization = Assert.Single(advisors.OfType<AuthorizationPipelineAdvisor<ListResourceQueryRequest<AnonymousEntity, Summary>, ListResultBase<Summary>>>());
        var listAdvisors = scope.ServiceProvider.GetServices<IResourceListRequestAdvisor<AnonymousEntity>>().OrderBy(advisor => advisor.Order).ToArray();
        var container = new ResourceRequestContainer<AnonymousEntity>();
        var calls = 0;

        var result = await authentication.AdviseAsync(context, envelope,
            ct => authorization.AdviseAsync(context, envelope, async token => {
                calls++;
                foreach (var advisor in listAdvisors) {
                    var advice = await advisor.AdviseAsync(context, request, container, null, token);
                    Assert.Equal(AdviseResult.Continue, advice);
                }

                return new ListResultBase<Summary>();
            }, ct), CancellationToken.None);
        var names = container.Query(new[] { new AnonymousEntity { Name = "allowed" }, new AnonymousEntity { Name = "denied" } }.AsQueryable()).Select(entity => entity.Name).ToArray();

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "allowed" }, names);
        access.VerifyNoOtherCalls();
        entitlement.VerifyAll();
        resolver.VerifyNoOtherCalls();
        matcher.VerifyNoOtherCalls();
    }

    [CanonicalName("entities/{entity}")]
    [Anonymous(Operations.List)]
    public sealed class AnonymousEntity : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
    [CanonicalName("entities/{entity}")]

    public sealed class Entity : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
    public sealed class MethodRequest : IRequest<MethodResponse>, IRequestPrincipal
    {
        public ClaimsPrincipal? Principal { get; set; }
    }

    public sealed class MethodResponse : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    public sealed class MethodHandler : IRequestHandler<MethodRequest, MethodResponse>
    {
        public Task<MethodResponse> HandleAsync(MethodRequest request, CancellationToken ct = default) {
            return Task.FromResult(new MethodResponse());
        }
    }

    private sealed class Detail : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    private sealed class Summary : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
