using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;
using Xunit;

namespace Schemata.Resource.Tests.Features;

public class SchemataResourceFeatureRegisterMethodShould
{
    [Fact]
    public void LeaveMethodsEmpty_WhenResourceHasNoResourceMethodAttribute() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<PlainEntity>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);
        Assert.Empty(options.Methods);
    }

    [Fact]
    public void StoreBuiltInMethods_WhenResourceIsSoftDeletable() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<SoftEntity>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);
        var methods = options.Methods[typeof(SoftEntity).TypeHandle]
                             .OrderBy(m => m.Verb)
                             .ToArray();

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
        var resource = new ResourceAttribute<PlainEntity>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);

        Assert.False(options.Methods.ContainsKey(typeof(PlainEntity).TypeHandle));
    }

    [Fact]
    public void PreserveUserDeclaredVerb_WhenSoftDeletableResourceOverridesBuiltIn() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<SoftOverrideEntity, SoftOverrideEntity>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);
        var methods = options.Methods[typeof(SoftOverrideEntity).TypeHandle]
                             .OrderBy(m => m.Verb)
                             .ToArray();

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
        var resource = new ResourceAttribute<SoftEntity> {
            Operations = [Operations.Get, Operations.List, Operations.Undelete, Operations.Expunge],
        };

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);
        var methods = options.Methods[typeof(SoftEntity).TypeHandle]
                             .OrderBy(m => m.Verb)
                             .ToArray();

        Assert.DoesNotContain(methods, m => m.Verb == "purge");
    }

    [Fact]
    public void PreserveUserDeclaredPurge_WhenSoftDeletableResourceOverridesBuiltIn() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<SoftPurgeOverrideEntity, SoftPurgeOverrideEntity>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);
        var method = options.Methods[typeof(SoftPurgeOverrideEntity).TypeHandle]
                            .Single(m => m.Verb == "purge");

        Assert.Equal(typeof(SoftPurgeHandler), method.Handler);
        Assert.Equal(ResourceMethodScope.Collection, method.Scope);
    }

    [Fact]
    public void StoreSingleMethod_WhenResourceDeclaresOneVerb() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<SingleVerbEntity, RunRequest>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options    = BuildOptions(services);
        var methods    = options.Methods[typeof(SingleVerbEntity).TypeHandle];
        var registered = Assert.Single(methods);
        Assert.Equal("run", registered.Verb);
        Assert.Equal(typeof(RunHandler), registered.Handler);
        Assert.Equal(ResourceMethodScope.Instance, registered.Scope);
    }

    [Fact]
    public void StoreSingleMethod_WhenResourceSuppliesProgrammaticVerb() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<PlainEntity, RunRequest> {
            Methods = [new("run", typeof(PlainRunHandler))],
        };

        SchemataResourceFeature.RegisterResource(services, resource);

        var options    = BuildOptions(services);
        var methods    = options.Methods[typeof(PlainEntity).TypeHandle];
        var registered = Assert.Single(methods);
        Assert.Equal("run", registered.Verb);
        Assert.Equal(typeof(PlainRunHandler), registered.Handler);
        Assert.Equal(ResourceMethodScope.Instance, registered.Scope);
    }

    [Fact]
    public void StoreSameMethodMetadata_ForAttributeAndProgrammaticRegistration() {
        var attributeServices = new ServiceCollection();
        SchemataResourceFeature.RegisterResource(attributeServices, new ResourceAttribute<SingleVerbEntity, RunRequest>());

        var programmaticServices = new ServiceCollection();
        SchemataResourceFeature.RegisterResource(
            programmaticServices,
            new ResourceAttribute<PlainEntity, RunRequest> {
                Methods = [new("run", typeof(PlainRunHandler))],
            });

        var attributeMethod = Assert.Single(BuildOptions(attributeServices).Methods[typeof(SingleVerbEntity).TypeHandle]);
        var explicitMethod  = Assert.Single(BuildOptions(programmaticServices).Methods[typeof(PlainEntity).TypeHandle]);

        Assert.Equal(attributeMethod.Verb, explicitMethod.Verb);
        Assert.Equal(attributeMethod.Scope, explicitMethod.Scope);
        Assert.Equal(ResourceHttpMethod.Post, explicitMethod.Method);
    }

    [Fact]
    public void StoreAllVerbs_WhenResourceDeclaresMultipleMethods() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<MultiVerbEntity, RunRequest>();

        SchemataResourceFeature.RegisterResource(services, resource);

        var options = BuildOptions(services);
        var methods = options.Methods[typeof(MultiVerbEntity).TypeHandle]
                             .OrderBy(m => m.Verb)
                             .ToArray();

        Assert.Equal(2, methods.Length);
        Assert.Equal("archive", methods[0].Verb);
        Assert.Equal(ResourceMethodScope.Instance, methods[0].Scope);
        Assert.Equal("batchCreate", methods[1].Verb);
        Assert.Equal(ResourceMethodScope.Collection, methods[1].Scope);
    }

    [Fact]
    public void RegisterHandlerInContainer_ForResolution() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<SingleVerbEntity, RunRequest>();

        SchemataResourceFeature.RegisterResource(services, resource);

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<RunHandler>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void Throw_WhenHandlerDoesNotImplementResourceMethodHandler() {
        var services = new ServiceCollection();
        var resource = new ResourceAttribute<InvalidHandlerEntity>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SchemataResourceFeature.RegisterResource(services, resource));

        Assert.Contains("IResourceMethodHandler", ex.Message, StringComparison.Ordinal);
        Assert.Contains("badVerb",                ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreserveLatestDeclaration_WhenSameVerbReRegistered() {
        var services    = new ServiceCollection();
        var firstPass   = new ResourceAttribute<SingleVerbEntity, RunRequest>();
        var secondPass  = new ResourceAttribute<SingleVerbEntity, RunRequest>();

        SchemataResourceFeature.RegisterResource(services, firstPass);
        SchemataResourceFeature.RegisterResource(services, secondPass);

        var options    = BuildOptions(services);
        var methods    = options.Methods[typeof(SingleVerbEntity).TypeHandle];
        var registered = Assert.Single(methods);
        Assert.Equal("run", registered.Verb);
    }

    [Fact]
    public void AssemblyLoadError_RegistersLoadableResources_NotSilentDropAll() {
        var services = new ServiceCollection();

        // Simulates a partially-loaded assembly (ReflectionTypeLoadException.Types): one type
        // resolved, one is null. The resolved resource must still register.
        SchemataResourceFeature.RegisterDiscoveredResources(services, [typeof(ScanResource), null]);

        var options = BuildOptions(services);
        Assert.True(options.Resources.ContainsKey(typeof(ScanResource).TypeHandle));
    }

    private static SchemataResourceOptions BuildOptions(IServiceCollection services) {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;
    }

    public sealed class PlainEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [Resource<ScanResource>]
    public sealed class ScanResource : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class SoftEntity : ICanonicalName, ISoftDelete
    {
        public string?   Name          { get; set; }
        public string?   CanonicalName { get; set; }
        public DateTime? DeleteTime    { get; set; }
        public DateTime? PurgeTime     { get; set; }
    }

    [ResourceMethod("undelete", typeof(SoftUndeleteHandler))]
    public sealed class SoftOverrideEntity : ICanonicalName, ISoftDelete
    {
        public string?   Name          { get; set; }
        public string?   CanonicalName { get; set; }
        public DateTime? DeleteTime    { get; set; }
        public DateTime? PurgeTime     { get; set; }
    }

    [ResourceMethod("purge", typeof(SoftPurgeHandler), ResourceMethodScope.Collection)]
    public sealed class SoftPurgeOverrideEntity : ICanonicalName, ISoftDelete
    {
        public string?   Name          { get; set; }
        public string?   CanonicalName { get; set; }
        public DateTime? DeleteTime    { get; set; }
        public DateTime? PurgeTime     { get; set; }
    }

    [ResourceMethod("run", typeof(RunHandler))]
    public sealed class SingleVerbEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [ResourceMethod("archive",     typeof(RunHandler))]
    [ResourceMethod("batchCreate", typeof(RunHandler), ResourceMethodScope.Collection)]
    public sealed class MultiVerbEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [ResourceMethod("badVerb", typeof(NotAHandler))]
    public sealed class InvalidHandlerEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class RunRequest : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class RunResponse : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class RunHandler : IResourceMethodHandler<SingleVerbEntity, RunRequest, RunResponse>
    {
        public ValueTask<RunResponse> InvokeAsync(
            string?           name,
            RunRequest        request,
            SingleVerbEntity? entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new RunResponse());
    }

    public sealed class PlainRunHandler : IResourceMethodHandler<PlainEntity, RunRequest, RunResponse>
    {
        public ValueTask<RunResponse> InvokeAsync(
            string?           name,
            RunRequest        request,
            PlainEntity?      entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new RunResponse());
    }

    public sealed class SoftUndeleteHandler : IResourceMethodHandler<SoftOverrideEntity, EmptyResourceRequest, SoftOverrideEntity>
    {
        public ValueTask<SoftOverrideEntity> InvokeAsync(
            string?              name,
            EmptyResourceRequest request,
            SoftOverrideEntity?  entity,
            ClaimsPrincipal?     principal,
            CancellationToken    ct
        ) => ValueTask.FromResult(entity ?? new SoftOverrideEntity());
    }

    public sealed class SoftPurgeHandler : IResourceMethodHandler<SoftPurgeOverrideEntity, PurgeRequest, PurgeResponse>
    {
        public ValueTask<PurgeResponse> InvokeAsync(
            string?                  name,
            PurgeRequest             request,
            SoftPurgeOverrideEntity? entity,
            ClaimsPrincipal?         principal,
            CancellationToken        ct
        ) => ValueTask.FromResult(new PurgeResponse());
    }

    public sealed class NotAHandler;
}
