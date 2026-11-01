using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core;
using Schemata.Modular;
using Xunit;

namespace Schemata.Core.Tests.Modular;

/// <summary>
///     Behavioral coverage for the Modular selection lifecycle across the real feature and
///     builder paths, per issue #62: the committed runner instance owns every phase, repeats
///     neither re-run discovery nor clear the owner, and conflicting selections are rejected
///     before any side effect.
/// </summary>
public class ModularSelectionShould
{
    private static IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();

    private static IWebHostEnvironment Environment { get; } = new Mock<IWebHostEnvironment>().Object;

    private sealed class CountingProvider : IModulesProvider
    {
        public static int Discoveries { get; set; }

        public IEnumerable<ModuleDescriptor> GetModules() {
            Discoveries++;
            return [];
        }
    }

    private sealed class CountingRunner : IModulesRunner
    {
        public int ApplicationCalls { get; private set; }

        public int EndpointCalls { get; private set; }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) { }

        public void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) {
            ApplicationCalls++;
        }

        public void ConfigureEndpoints(IApplicationBuilder app, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, IConfiguration configuration, IWebHostEnvironment environment) {
            EndpointCalls++;
        }
    }

    private sealed class OtherCountingRunner : IModulesRunner
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) { }

        public void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) { }

        public void ConfigureEndpoints(IApplicationBuilder app, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, IConfiguration configuration, IWebHostEnvironment environment) { }
    }

    [Fact]
    public void Builder_Instance_Entry_Rejects_A_Different_Instance_Before_Registration() {
        var builder = new SchemataBuilder(Configuration, Environment);
        var first   = new CountingRunner();
        var second  = new CountingRunner();

        builder.UseModular(first);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.UseModular(second));
        Assert.Contains(nameof(CountingRunner), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_Instance_Entry_Is_Idempotent_For_The_Same_Instance() {
        var builder = new SchemataBuilder(Configuration, Environment);
        var runner  = new CountingRunner();

        builder.UseModular(runner);
        builder.UseModular(runner);

        Assert.True(builder.HasFeature<Schemata.Modular.Features.SchemataModulesFeature<DefaultModulesProvider, CountingRunner>>());
    }

    [Fact]
    public void Builder_Instance_Entry_Rejects_A_Different_Runner_Type() {
        var builder = new SchemataBuilder(Configuration, Environment);

        builder.UseModular(new CountingRunner());

        Assert.Throws<InvalidOperationException>(() => builder.UseModular<OtherCountingRunner>(new()));
    }

    [Fact]
    public void Explicit_Selection_Owns_Runtime_Despite_A_PreRegistered_Runner() {
        CountingProvider.Discoveries = 0;
        var services = new ServiceCollection();
        var schemata = new SchemataOptions();
        var unrelated = new CountingRunner();
        var selected = new CountingRunner();
        services.AddSingleton<IModulesRunner>(unrelated);
        services.AddSchemataModules<CountingProvider, CountingRunner>(schemata, Configuration, Environment, selected);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IModulesRunner>().ConfigureApplication(new Mock<IApplicationBuilder>().Object, Configuration, Environment);
        Assert.Equal(1, selected.ApplicationCalls);
        Assert.Equal(0, unrelated.ApplicationCalls);
        Assert.Equal(1, CountingProvider.Discoveries);
    }

    [Fact]
    public void Repeated_Invoke_Retains_The_Owner_And_Runs_Each_Phase_Once() {
        CountingProvider.Discoveries = 0;
        var builder  = new SchemataBuilder(Configuration, Environment);
        var runner   = new CountingRunner();
        var services = new ServiceCollection();
        builder.UseModular<CountingRunner, CountingProvider>(runner);

        // Two Invoke cycles re-run ConfigureServices on the same registered feature instance.
        builder.Invoke(services);
        builder.Invoke(services);

        Assert.Equal(1, CountingProvider.Discoveries);
        var provider = services.BuildServiceProvider();
        Assert.Same(runner, provider.GetRequiredService<IModulesRunner>());

        // Both runtime phases resolve the committed DI runner.
        var registered = builder.Options.GetFeatures()!.Values
                           .Single(f => f is Schemata.Modular.Features.SchemataModulesFeature<CountingProvider, CountingRunner>);
        var app = new Mock<IApplicationBuilder>();
        app.SetupGet(a => a.ApplicationServices).Returns(provider);
        var endpoints = new Mock<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder>().Object;
        registered.ConfigureApplication(app.Object, Configuration, Environment);
        registered.ConfigureEndpoints(app.Object, endpoints, Configuration, Environment);

        Assert.Equal(1, CountingProvider.Discoveries);
        Assert.Equal(1, runner.ApplicationCalls);
        Assert.Equal(1, runner.EndpointCalls);

        // A duplicate feature instance over the same selection adds no ConfigureServices side
        // effects, and its runtime phases resolve the same committed DI runner.
        var duplicate = new Schemata.Modular.Features.SchemataModulesFeature<CountingProvider, CountingRunner>();
        duplicate.ConfigureServices(services, builder.Options, new(), Configuration, Environment);
        duplicate.ConfigureApplication(app.Object, Configuration, Environment);
        duplicate.ConfigureEndpoints(app.Object, endpoints, Configuration, Environment);

        Assert.Equal(1, CountingProvider.Discoveries);
        Assert.Equal(2, runner.ApplicationCalls);
        Assert.Equal(2, runner.EndpointCalls);
        Assert.Same(runner, provider.GetRequiredService<IModulesRunner>());
    }
}
