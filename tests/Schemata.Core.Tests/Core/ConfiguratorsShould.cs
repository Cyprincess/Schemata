using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Core.Features;
using Xunit;

namespace Schemata.Core.Tests.Core;

public class ConfiguratorsShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public void Invoke_AppliesStagedAndDeferredOptionsOnce(bool withFeature) {
        var services = new ServiceCollection();
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), Mock.Of<IWebHostEnvironment>());
        if (withFeature) {
            builder.AddFeature<SchemataRoutingFeature>();
        }

        builder.ConfigureServices(staged => staged.Configure<TestOptions>(options => options.Value += 21));
        builder.Configure<TestOptions>(options => options.Value *= 2);

        builder.Invoke(services);
        builder.Invoke(services);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(42, provider.GetRequiredService<IOptions<TestOptions>>().Value.Value);
    }

    [Fact]
    public void Get_AfterSet_InvokesStoredAction() {
        var configurators = new Configurators();
        var called        = false;

        configurators.Set<string>(_ => called = true);
        var action = configurators.Get<string>();
        action("test");

        Assert.True(called);
    }

    [Fact]
    public void Set_Chain_MultipleActions() {
        var configurators = new Configurators();
        var calls         = new List<int>();

        configurators.Set<string>(_ => calls.Add(1));
        configurators.Set<string>(_ => calls.Add(2));

        var action = configurators.Get<string>();
        action("test");

        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public void Get_AfterSet_TwoArgAction_PassesBothArguments() {
        var configurators = new Configurators();
        var captured      = "";

        configurators.Set<string, int>((s, i) => captured = $"{s}-{i}");
        var action = configurators.Get<string, int>();
        action("hello", 42);

        Assert.Equal("hello-42", captured);
    }

    [Fact]
    public void TryGet_Missing_ReturnFalse() {
        var configurators = new Configurators();

        var found = configurators.TryGet<string>(out var action);

        Assert.False(found);
        Assert.Null(action);
    }

    [Fact]
    public void Get_Missing_ThrowKeyNotFoundException() {
        var configurators = new Configurators();

        Assert.Throws<KeyNotFoundException>(() => configurators.Get<string>());
    }

    [Fact]
    public void Pop_RemoveAndReturn() {
        var configurators = new Configurators();
        configurators.Set<string>(_ => { });

        var action = configurators.Pop<string>();

        Assert.NotNull(action);
        Assert.False(configurators.TryGet<string>(out var _));
    }

    [Fact]
    public void PopOrDefault_Missing_ReturnNoOp() {
        var configurators = new Configurators();

        var action = configurators.PopOrDefault<string>();

        Assert.NotNull(action);
        action("test");
    }

    [Fact]
    public void PopOrDefault_TwoType_Missing_ReturnNoOp() {
        var configurators = new Configurators();

        var action = configurators.PopOrDefault<string, int>();

        Assert.NotNull(action);
        action("test", 42);
    }

    [Fact]
    public void Invoke_Rejects_An_Unconsumed_Two_Parameter_Configurator() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), Mock.Of<IWebHostEnvironment>());
        builder.Configure<string, TestOptions>((_, _) => { });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Invoke(new ServiceCollection()));

        Assert.Contains(typeof(TestOptions).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_Skips_A_Two_Parameter_Configurator_Consumed_By_Its_Feature() {
        var services = new ServiceCollection();
        var builder  = new SchemataBuilder(new ConfigurationBuilder().Build(), Mock.Of<IWebHostEnvironment>());
        var applied  = false;
        builder.Configure<string, TestOptions>((_, _) => applied = true);
        builder.Configurators.PopOrDefault<string, TestOptions>();

        builder.Invoke(services);

        Assert.False(applied);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(0, provider.GetRequiredService<IOptions<TestOptions>>().Value.Value);
    }

    [Fact]
    public void AddFeature_Registers_A_Generic_Dependency_On_First_Registration() {
        var schemata = new SchemataOptions();

        schemata.AddFeature<DependentFeature>();

        Assert.True(schemata.HasFeature<DependentFeature>());
        Assert.True(schemata.HasFeature<DependencyFeature>());
    }

    [Fact]
    public void AddSchemataJsonSerializer_Shapes_Mvc_Json_Options_Without_A_Controllers_Feature() {
        var services = new ServiceCollection();

        services.AddSchemataJsonSerializer(_ => { });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.JsonSerializerOptions.PropertyNamingPolicy);
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.JsonSerializerOptions.DictionaryKeyPolicy);
        Assert.Equal(JsonNumberHandling.AllowReadingFromString, options.JsonSerializerOptions.NumberHandling);
    }

    [Fact]
    public void ReplaceLoggerFactory_Retargets_The_Cached_Builder_Logger() {
        var options = new SchemataOptions();
        var factory = new Mock<ILoggerFactory>();
        factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
        _ = options.Logger;

        options.ReplaceLoggerFactory(factory.Object);
        options.Logger.LogInformation("swapped");

        factory.Verify(f => f.CreateLogger(It.IsAny<string>()), Times.Once);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Honor_Consumer_Proxy_Trust_Through_The_Middleware_Pipeline() {
        var context = await InvokeForwardedHeaders(
            options => {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                options.KnownProxies.Clear();
                options.KnownProxies.Add(IPAddress.Parse("203.0.113.1"));
#if NET10_0_OR_GREATER
                options.KnownIPNetworks.Clear();
#else
                options.KnownNetworks.Clear();
#endif
            },
            request => request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = "198.51.100.7");

        Assert.Equal(IPAddress.Loopback, context.Connection.RemoteIpAddress);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Honor_Consumer_Header_Selection_Through_The_Middleware_Pipeline() {
        var context = await InvokeForwardedHeaders(
            options => options.ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            request => {
                request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName]   = "198.51.100.7";
                request.Headers[ForwardedHeadersDefaults.XForwardedProtoHeaderName] = "https";
            });

        Assert.Equal(IPAddress.Parse("198.51.100.7"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Trust_Only_Loopback_Proxies_By_Default() {
        var context = await InvokeForwardedHeaders(
            null,
            request => {
                request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName]   = "198.51.100.7";
                request.Headers[ForwardedHeadersDefaults.XForwardedProtoHeaderName] = "https";
            });

        Assert.Equal(IPAddress.Parse("198.51.100.7"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    private static async Task<DefaultHttpContext> InvokeForwardedHeaders(
        Action<ForwardedHeadersOptions>? configure,
        Action<HttpRequest>                prepare
    ) {
        var configuration = new ConfigurationBuilder().Build();
        var environment   = Mock.Of<IWebHostEnvironment>();
        var services      = new ServiceCollection();
        var builder       = new SchemataBuilder(configuration, environment);

        builder.UseForwardedHeaders(configure);
        builder.Invoke(services);
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        new SchemataForwardedHeadersFeature().ConfigureApplication(app, configuration, environment);
        var pipeline = app.Build();

        var context = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Loopback } };
        context.Request.Scheme = "http";
        prepare(context.Request);
        await pipeline(context);
        return context;
    }

    public sealed class DependencyFeature : FeatureBase { }

    [DependsOn<DependencyFeature>]
    public sealed class DependentFeature : FeatureBase { }

    public sealed class TestOptions
    {
        public int Value { get; set; }
    }
}
