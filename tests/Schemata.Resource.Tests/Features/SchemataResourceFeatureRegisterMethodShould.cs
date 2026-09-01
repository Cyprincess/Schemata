using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;
using Xunit;

namespace Schemata.Resource.Tests.Features;

public class SchemataResourceFeatureRegisterMethodShould
{
    [Fact]
    public void LeaveMethodsEmpty_WhenResourceHasNoResourceMethodAttribute() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<PlainEntity>();

        services.AddResource(resource, registry);

        Assert.Empty(registry.GetMethods(typeof(PlainEntity)));
    }

    [Fact]
    public void StoreBuiltInMethods_WhenResourceIsSoftDeletable() {
        var services = new ServiceCollection();
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
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<PlainEntity>();

        services.AddResource(resource, registry);

        Assert.Empty(registry.GetMethods(typeof(PlainEntity)));
    }

    [Fact]
    public void PreserveUserDeclaredVerb_WhenSoftDeletableResourceOverridesBuiltIn() {
        var services = new ServiceCollection();
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
        var registry = new ResourceRegistry();
        var resource = new ResourceAttribute<InvalidHandlerEntity>();

        var ex
            = Assert.Throws<InvalidOperationException>(() => services.AddResource(resource, registry));

        Assert.Contains("IRequest", ex.Message, StringComparison.Ordinal);
        Assert.Contains("badVerb", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterOneMethod_WhenTheSameResourceIsDeclaredTwice() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<SingleVerbEntity, RunRequest>(), registry);
        services.AddResource(new ResourceAttribute<SingleVerbEntity, RunRequest>(), registry);

        var registered = Assert.Single(registry.GetMethods(typeof(SingleVerbEntity)));
        Assert.Equal("run", registered.Verb);
    }

    [Fact]
    public void LeaveAnAttributedEntityUnregistered_UntilItIsAddedExplicitly() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<PlainEntity>(), registry);

        Assert.NotNull(registry.GetResource(typeof(PlainEntity)));
        Assert.Null(registry.GetResource(typeof(ScanResource)));
    }

    [Fact]
    public void Share_One_Registry_Across_Builders_Over_The_Same_Options() {
        var schemata = new SchemataOptions();
        var services = new ServiceCollection();

        new SchemataResourceBuilder(schemata, services).AddResource<ScanResource>();
        new SchemataResourceBuilder(schemata, services).Use<PlainEntity, PlainEntity, PlainEntity, PlainEntity>();

        using var provider = services.BuildServiceProvider();
        var       registry = provider.GetRequiredService<IResourceRegistry>();

        Assert.NotNull(registry.GetResource(typeof(ScanResource)));
        Assert.NotNull(registry.GetResource(typeof(PlainEntity)));
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

        #endregion
    }

    #endregion

    #region Nested type: PlainRunHandler

    public sealed class PlainRunHandler : IRequestHandler<PlainRunRequest, RunResponse>
    {
        #region IRequestHandler<PlainRunRequest,RunResponse> Members

        public Task<RunResponse> HandleAsync(
            PlainRunRequest       request,
            CancellationToken ct = default
        ) {
            return Task.FromResult(new RunResponse());
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

    public sealed class RunResponse : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

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
