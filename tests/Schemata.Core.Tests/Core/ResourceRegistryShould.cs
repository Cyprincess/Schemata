using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Xunit;

namespace Schemata.Core.Tests.Core;

[Trait("Layer", "Unit")]
public class ResourceRegistryShould
{
    [Theory]
    [InlineData("resources")]
    [InlineData("resource")]
    [InlineData("methods")]
    public void RuntimeRead_SealsRegistryAndRejectsLaterRegistration(string read) {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Widget, Widget, Widget, Widget>(), []);
        switch (read) {
            case "resources": _ = registry.Resources; break;
            case "resource": _ = registry.GetResource(typeof(Widget)); break;
            case "methods": _ = registry.GetMethods(typeof(Widget)); break;
        }

        var error = Assert.Throws<InvalidOperationException>(() => registry.Register(
            new ServiceCollection(), new ResourceAttribute<Other, Other, Other, Other>(), []));

        Assert.Contains(typeof(Other).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(Widget), Assert.Single(registry.Resources).Entity);
        Assert.Null(registry.GetResource(typeof(Other)));
    }

    [Fact]
    public void Registration_CopiesInputsAndPublishesOneImmutableSnapshot() {
        var endpoints = new[] { "Http" };
        var operations = new[] { Operations.Get };
        var method = new ResourceMethodAttribute("inspect", typeof(Widget)) { Method = ResourceHttpMethod.Get };
        var methods = new[] { method };
        var resource = new ResourceAttribute<Widget> {
            Endpoints = endpoints, Operations = operations, AuthenticationScheme = "original", DefaultPageSize = 7,
        };
        var registry = new ResourceRegistry();
        registry.Add(resource, methods);
        endpoints[0] = "Grpc";
        operations[0] = Operations.Delete;
        method.Method = ResourceHttpMethod.Post;
        methods[0] = new("replace", typeof(Other));
        resource.AuthenticationScheme = "changed";
        resource.DefaultPageSize = 99;

        var registration = registry.GetResource(typeof(Widget))!;
        Assert.Same(registration, Assert.Single(registry.Resources));
        Assert.Same(registration.Methods, registry.GetMethods(typeof(Widget)));
        Assert.Equal(new[] { "Http" }, registration.Endpoints);
        Assert.Equal(new[] { Operations.Get }, registration.Operations);
        Assert.Equal("original", registration.AuthenticationScheme);
        Assert.Equal(7, registration.DefaultPageSize);
        Assert.Equal(ResourceHttpMethod.Get, Assert.Single(registration.Methods).Method);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)registration.Endpoints!)[0] = "Other");
        Assert.Throws<NotSupportedException>(() => ((IList<Operations>)registration.Operations!)[0] = Operations.Delete);
        resource.Endpoints = ["Late"];
        resource.Operations = [Operations.Create];
        method.Method = ResourceHttpMethod.Post;
        Assert.Equal(new[] { "Http" }, registry.GetResource(typeof(Widget))!.Endpoints);
        Assert.Equal(new[] { Operations.Get }, registry.GetResource(typeof(Widget))!.Operations);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceRegistration>)registry.Resources).Clear());
    }

    [Theory]
    [InlineData("tuple")]
    [InlineData("scheme")]
    [InlineData("page")]
    [InlineData("maximum")]
    [InlineData("total")]
    [InlineData("operations")]
    [InlineData("handler")]
    [InlineData("scope")]
    [InlineData("http")]
    public void ConflictingRegistration_LeavesThePublishedContractUnchanged(string conflict) {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Widget> {
            AuthenticationScheme = "scheme", DefaultPageSize = 7, MaxPageSize = 10,
            TotalSize = TotalSizeMode.Default, Operations = [], Endpoints = ["Http"],
        }, [new("inspect", typeof(Widget))]);
        var incoming = conflict == "tuple" ? new ResourceAttribute(typeof(Widget), typeof(Other)) : new ResourceAttribute<Widget>();
        incoming.Endpoints = ["Grpc"];
        switch (conflict) {
            case "scheme": incoming.AuthenticationScheme = "other"; break;
            case "page": incoming.DefaultPageSize = 8; break;
            case "maximum": incoming.MaxPageSize = 11; break;
            case "total": incoming.TotalSize = TotalSizeMode.None; break;
            case "operations": incoming.Operations = [Operations.Get]; break;
        }
        var method = new ResourceMethodAttribute("inspect", conflict == "handler" ? typeof(Other) : typeof(Widget),
            conflict == "scope" ? ResourceMethodScope.Collection : ResourceMethodScope.Instance) {
            Method = conflict == "http" ? ResourceHttpMethod.Get : ResourceHttpMethod.Post,
        };
        Assert.Throws<InvalidOperationException>(() => registry.Add(incoming, [method]));
        var registration = registry.GetResource(typeof(Widget))!;
        Assert.Equal(typeof(Widget), registration.Request);
        Assert.Equal("scheme", registration.AuthenticationScheme);
        Assert.Equal(7, registration.DefaultPageSize);
        Assert.Equal(10, registration.MaxPageSize);
        Assert.Equal(TotalSizeMode.Default, registration.TotalSize);
        Assert.Empty(registration.Operations!);
        Assert.Equal(new[] { "Http" }, registration.Endpoints);
        var registered = Assert.Single(registration.Methods);
        Assert.Equal(typeof(Widget), registered.Handler);
        Assert.Equal(ResourceMethodScope.Instance, registered.Scope);
        Assert.Equal(ResourceHttpMethod.Post, registered.Method);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Merge_UsesUnrestrictedEndpointsAndSupplementsUnsetPolicy(bool unrestrictedFirst) {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Widget> { Endpoints = unrestrictedFirst ? null : ["Http"] }, []);
        registry.Add(new ResourceAttribute<Widget> {
            Endpoints = unrestrictedFirst ? ["Grpc"] : null, Operations = [], DefaultPageSize = 73,
            AuthenticationScheme = "tenant-auth", TotalSize = TotalSizeMode.None,
        }, [new("inspect", typeof(Widget)), new("inspect", typeof(Widget))]);
        var registration = registry.GetResource(typeof(Widget))!;
        Assert.Null(registration.Endpoints);
        Assert.Empty(registration.Operations!);
        Assert.Equal(73, registration.DefaultPageSize);
        Assert.Equal("tenant-auth", registration.AuthenticationScheme);
        Assert.Equal(TotalSizeMode.None, registration.TotalSize);
        Assert.Single(registration.Methods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualPolicyRegistration_UnionsOwnedEndpointsAndDeduplicatesMethods(bool reverse) {
        var registry = new ResourceRegistry();
        var first = new ResourceAttribute<Widget> {
            Endpoints = [reverse ? "Grpc" : "Http"], Operations = [Operations.List, Operations.Get], AuthenticationScheme = "",
        };
        var second = new ResourceAttribute<Widget> {
            Endpoints = [reverse ? "Http" : "Grpc"], Operations = [Operations.Get, Operations.List, Operations.Get], AuthenticationScheme = "",
        };
        registry.Add(first, [new("inspect", typeof(Widget))]);
        registry.Add(second, [new("inspect", typeof(Widget))]);
        var registration = registry.GetResource(typeof(Widget))!;
        Assert.Equal(reverse ? new[] { "Grpc", "Http" } : new[] { "Http", "Grpc" }, registration.Endpoints);
        Assert.Equal(new[] { Operations.List, Operations.Get }, registration.Operations);
        Assert.Equal("", registration.AuthenticationScheme);
        Assert.Single(registration.Methods);
        Assert.Single(first.Endpoints!);
        Assert.Single(second.Endpoints!);
    }

    [Fact]
    public void ExplicitEmptyEndpoints_KeepUnrestrictedExposureAfterRestrictedSupplement() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Widget> { Endpoints = [] }, []);
        registry.Add(new ResourceAttribute<Widget> { Endpoints = ["Grpc"] }, []);
        Assert.Null(registry.GetResource(typeof(Widget))!.Endpoints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectRegistration_NullEndpointsUseDeclarations_ExplicitEmptyStaysUnrestricted(bool explicitEmpty) {
        var registry = new ResourceRegistry();
        var input = new ResourceAttribute<HttpWidget> { Endpoints = explicitEmpty ? [] : null };
        registry.Register(new ServiceCollection(), input, []);
        input.Endpoints = [GrpcResourceAttribute.Name];

        var registration = registry.GetResource(typeof(HttpWidget))!;
        if (explicitEmpty) {
            Assert.Null(registration.Endpoints);
        } else {
            Assert.Equal(new[] { HttpResourceAttribute.Name }, registration.Endpoints);
        }
    }

    [Fact]
    public void UnrestrictedSupplement_DoesNotReapplyDeclarationsDuringMerge() {
        var registry = new ResourceRegistry();
        registry.Register(new ServiceCollection(), new ResourceAttribute<HttpWidget>(), []);
        registry.Register(new ServiceCollection(), new ResourceAttribute<HttpWidget> { Endpoints = [] }, []);

        Assert.Null(registry.GetResource(typeof(HttpWidget))!.Endpoints);
    }

    [Fact]
    public void Seal_RejectsEveryConfigurationMutationWithoutChangingChoices() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Widget>(), []);
        var registration = registry.GetResource(typeof(Widget));
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => registry.ActivateAuthentication(services));
        Assert.Throws<InvalidOperationException>(() => registry.ActivateAuthorization(services));
        Assert.Throws<InvalidOperationException>(() => registry.ActivateAuthorization(services, typeof(Widget)));
        Assert.Throws<InvalidOperationException>(() => registry.WithoutCreateValidation(services));
        Assert.Throws<InvalidOperationException>(() => registry.WithoutUpdateValidation(services));
        Assert.Throws<InvalidOperationException>(() => registry.WithoutFreshness(services));
        Assert.Same(registration, registry.GetResource(typeof(Widget)));
        Assert.True(registry.CreateValidation);
        Assert.True(registry.UpdateValidation);
        Assert.True(registry.Freshness);
    }

    public sealed class Widget : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Other : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [HttpResource]
    public sealed class HttpWidget : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }
}
