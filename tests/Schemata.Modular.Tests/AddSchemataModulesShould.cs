using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core;
using Schemata.Modular;
using Schemata.Modular.Features;
using Xunit;

namespace Schemata.Modular.Tests;

public class AddSchemataModulesShould
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private static IWebHostEnvironment Environment { get; } = new Mock<IWebHostEnvironment>().Object;

    [Fact]
    public void Run_The_Runners_ConfigureServices_Exactly_Once_For_Repeated_Calls() {
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();
        TestRunner.Reset();

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);
        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);

        Assert.Equal(1, TestRunner.Calls);
    }

    [Fact]
    public void Fail_Explicitly_When_A_Second_Call_Selects_A_Different_Runner() {
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddSchemataModules<TestProvider, OtherRunner>(schemata, Configuration, Environment));
        Assert.Contains(nameof(TestRunner), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_Run_Discovery_Again_For_Repeated_Same_Selection() {
        TestRunner.Reset();
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();
        TestProvider.Discoveries = 0;

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);
        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);

        Assert.Equal(1, TestProvider.Discoveries);
        Assert.Equal(1, TestRunner.Calls);
    }

    [Fact]
    public void Fail_Explicitly_When_A_Second_Call_Selects_A_Different_Provider() {
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddSchemataModules<OtherProvider, TestRunner>(schemata, Configuration, Environment));
        Assert.Contains(nameof(TestProvider), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_Pollute_The_Committed_Snapshot_When_Rejecting_A_Conflicting_Selection() {
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment);
        schemata.SetModules([new("Committed", typeof(AddSchemataModulesShould).Assembly, typeof(AddSchemataModulesShould), typeof(TestProvider))]);

        Assert.Throws<InvalidOperationException>(
            () => services.AddSchemataModules<OtherProvider, OtherRunner>(schemata, Configuration, Environment));

        var modules = schemata.GetModules();
        Assert.NotNull(modules);
        Assert.Equal("Committed", Assert.Single(modules).Name);
    }

    [Fact]
    public void Use_The_Supplied_Instance_For_Every_Selection_Decision() {
        TestRunner.Reset();
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();
        var runner    = new TestRunner();

        var committed = services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment, runner);

        Assert.Same(runner, committed);
        Assert.Equal(1, TestRunner.Calls);
        Assert.Same(runner, services.BuildServiceProvider().GetRequiredService<IModulesRunner>());
    }

    [Fact]
    public void Fail_Explicitly_When_A_Second_Instance_Of_The_Same_Runner_Type_Is_Selected() {
        var services = new ServiceCollection();
        var schemata = new SchemataOptions();
        var first    = new TestRunner();
        var second   = new TestRunner();

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment, first);

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment, second));
        Assert.Contains(nameof(TestRunner), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Return_Null_For_An_Idempotent_Repeat_Of_The_Same_Instance() {
        TestRunner.Reset();
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();
        var runner    = new TestRunner();

        services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment, runner);
        var repeated = services.AddSchemataModules<TestProvider, TestRunner>(schemata, Configuration, Environment, runner);

        Assert.Null(repeated);
        Assert.Equal(1, TestRunner.Calls);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public void Run_The_Runtime_Phases_On_The_DI_Registered_Runner() {
        TestRunner.Reset();
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();
        var feature   = new SchemataModulesFeature<TestProvider, TestRunner>();

        feature.ConfigureServices(services, schemata, new Configurators(), Configuration, Environment);

        using var provider = services.BuildServiceProvider();
        var app = new Mock<Microsoft.AspNetCore.Builder.IApplicationBuilder>();
        app.SetupGet(value => value.ApplicationServices).Returns(provider);
        var endpoints = new Mock<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder>();

        feature.ConfigureApplication(app.Object, Configuration, Environment);
        feature.ConfigureEndpoints(app.Object, endpoints.Object, Configuration, Environment);

        var committed = provider.GetRequiredService<IModulesRunner>();
        Assert.Equal(1, ((TestRunner)committed).ApplicationCalls);
        Assert.Equal(1, ((TestRunner)committed).EndpointsCalls);
        Assert.Equal(1, TestRunner.Calls);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public void Keep_Runtime_Phases_Working_After_A_Repeated_ConfigureServices() {
        TestRunner.Reset();
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();

        new SchemataModulesFeature<TestProvider, TestRunner>()
           .ConfigureServices(services, schemata, new Configurators(), Configuration, Environment);
        var feature = new SchemataModulesFeature<TestProvider, TestRunner>();
        feature.ConfigureServices(services, schemata, new Configurators(), Configuration, Environment);

        using var provider = services.BuildServiceProvider();
        var app = new Mock<Microsoft.AspNetCore.Builder.IApplicationBuilder>();
        app.SetupGet(value => value.ApplicationServices).Returns(provider);

        feature.ConfigureApplication(app.Object, Configuration, Environment);

        Assert.Equal(1, TestRunner.Calls);
        Assert.Equal(1, ((TestRunner)provider.GetRequiredService<IModulesRunner>()).ApplicationCalls);
    }

    private sealed class TestProvider : IModulesProvider
    {
        public static int Discoveries { get; set; }

        public IEnumerable<ModuleDescriptor> GetModules() {
            Discoveries++;
            return [];
        }
    }

    private sealed class OtherProvider : IModulesProvider
    {
        public IEnumerable<ModuleDescriptor> GetModules() { return []; }
    }

    private sealed class TestRunner : IModulesRunner
    {
        private static int _calls;

        public static int Calls => _calls;

        public int ApplicationCalls { get; private set; }

        public int EndpointsCalls { get; private set; }

        public static void Reset() { _calls = 0; }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) {
            _calls++;
        }

        public void ConfigureApplication(Microsoft.AspNetCore.Builder.IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) {
            ApplicationCalls++;
        }

        public void ConfigureEndpoints(Microsoft.AspNetCore.Builder.IApplicationBuilder app, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, IConfiguration configuration, IWebHostEnvironment environment) {
            EndpointsCalls++;
        }
    }

    private sealed class OtherRunner : IModulesRunner
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) { }

        public void ConfigureApplication(Microsoft.AspNetCore.Builder.IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) { }

        public void ConfigureEndpoints(Microsoft.AspNetCore.Builder.IApplicationBuilder app, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, IConfiguration configuration, IWebHostEnvironment environment) { }
    }
}
