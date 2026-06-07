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

    private static SchemataResourceOptions BuildOptions(IServiceCollection services) {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;
    }

    public sealed class PlainEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
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
            SingleVerbEntity  entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new RunResponse());
    }

    public sealed class NotAHandler;
}
