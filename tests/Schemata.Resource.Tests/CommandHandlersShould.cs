using Schemata.Core.Building;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Commands;
using Xunit;

namespace Schemata.Resource.Tests;

public class CommandHandlersShould
{
    [Fact]
    public void Register_Keyed_Default_And_Unkeyed_Alias_For_Each_Standard_Operation() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddSchemataResources();
        services.AddResource(new ResourceAttribute<Entity, Request, Detail, Summary>(), registry);

        AssertHandler<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(services);
        AssertHandler<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>(services);
        AssertHandler<ListResourceQueryRequest<Entity, Summary>, ListResultBase<Summary>>(services);
        AssertHandler<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(services);
        AssertHandler<DeleteResourceRequest<Entity, Detail>, DeleteResultBase<Detail>>(services);
    }

    [Fact]
    public void Register_Distinct_Handlers_When_Resources_Share_Dto_Types() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddSchemataResources();
        services.AddResource(new ResourceAttribute<Entity, Request, Detail, Summary>(), registry);
        services.AddResource(new ResourceAttribute<SecondEntity, Request, Detail, Summary>(), registry);

        AssertHandler<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(services);
        AssertHandler<CreateResourceRequest<SecondEntity, Request, Detail>, CreateResultBase<Detail>>(services);
        AssertHandler<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>(services);
        AssertHandler<GetResourceQueryRequest<SecondEntity, Detail>, GetResultBase<Detail>>(services);
        AssertHandler<ListResourceQueryRequest<Entity, Summary>, ListResultBase<Summary>>(services);
        AssertHandler<ListResourceQueryRequest<SecondEntity, Summary>, ListResultBase<Summary>>(services);
        AssertHandler<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(services);
        AssertHandler<UpdateResourceRequest<SecondEntity, Request, Detail>, UpdateResultBase<Detail>>(services);
        AssertHandler<DeleteResourceRequest<Entity, Detail>, DeleteResultBase<Detail>>(services);
        AssertHandler<DeleteResourceRequest<SecondEntity, Detail>, DeleteResultBase<Detail>>(services);
    }

    [Fact]
    public void Describe_Direct_Custom_Method_Handler() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<Entity, Request, Detail, Summary> {
            Methods = [new("run", typeof(RunHandler))],
        };

        services.AddResource(resource, registry);

        var method     = Assert.Single(registry.GetMethods(typeof(Entity)));
        var descriptor = ResourceMethodHandlerHelper.Describe(typeof(Entity), method.Handler);

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(Entity), descriptor.Entity);
        Assert.Equal(typeof(RunResourceRequest), descriptor.Request);
        Assert.Equal(typeof(Detail), descriptor.Response);
        Assert.Equal(typeof(RunHandler), descriptor.Handler);
        Assert.Contains(services, service =>
            service.ServiceType == typeof(IRequestHandler<RunResourceRequest, Detail>)
         && service.ImplementationType == typeof(RunHandler)
         && service.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Standard_Requests_Expose_The_Mutable_Principal_Contract() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        IRequestPrincipal[] requests = [
            new CreateResourceRequest<Entity, Request, Detail>(new(), null),
            new GetResourceQueryRequest<Entity, Detail>(new(), null),
            new ListResourceQueryRequest<Entity, Summary>(new(), null),
            new UpdateResourceRequest<Entity, Request, Detail>("entities/1", new(), null),
            new DeleteResourceRequest<Entity, Detail>("entities/1", null, null),
        ];

        foreach (var request in requests) {
            request.Principal = principal;
            Assert.Same(principal, request.Principal);
        }
    }

    [Fact]
    public void Register_Distinct_BuiltIn_Custom_Method_Handlers_Per_Resource() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<SoftEntity, Request, Detail, Summary>(), registry);
        services.AddResource(new ResourceAttribute<SecondSoftEntity, Request, Detail, Summary>(), registry);

        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestHandler<ExpungeResourceRequest<SoftEntity>, EmptyResourceResponse>));
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestHandler<ExpungeResourceRequest<SecondSoftEntity>, EmptyResourceResponse>));
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestHandler<PurgeResourceRequest<SoftEntity>, Operation>));
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestHandler<PurgeResourceRequest<SecondSoftEntity>, Operation>));
    }


    private static void AssertHandler<TRequest, TResponse>(IServiceCollection services)
        where TRequest : IRequest<TResponse> {
        var serviceType = typeof(IRequestHandler<TRequest, TResponse>);

        Assert.Contains(services, descriptor => descriptor.ServiceType == serviceType
                                             && Equals(descriptor.ServiceKey, ResourceConstants.Handlers.Default));
        Assert.Contains(services, descriptor => descriptor.ServiceType == serviceType
                                             && descriptor.ServiceKey is null);
    }

    [CanonicalName("entities/{entity}")]
    private sealed class Entity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("second_entities/{second_entity}")]
    private sealed class SecondEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("soft_entities/{soft_entity}")]
    private sealed class SoftEntity : ICanonicalName, ISoftDelete
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public DateTime? DeleteTime  { get; set; }
        public DateTime? PurgeTime   { get; set; }
    }

    [CanonicalName("second_soft_entities/{second_soft_entity}")]
    private sealed class SecondSoftEntity : ICanonicalName, ISoftDelete
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public DateTime? DeleteTime  { get; set; }
        public DateTime? PurgeTime   { get; set; }
    }

    private sealed class Request : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class Detail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class RunResourceRequest : ICommand<Detail>, IRequestPrincipal
    {
        public ClaimsPrincipal? Principal { get; set; }
    }

    private sealed class RunHandler : IRequestHandler<RunResourceRequest, Detail>
    {
        public Task<Detail> HandleAsync(RunResourceRequest request, CancellationToken ct = default) {
            return Task.FromResult(new Detail());
        }
    }
}
