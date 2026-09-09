using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdvancedProtocolFeatureOptionsShould
{
    [Fact]
    public void Apply_Pushed_Authorization_Request_Options_Through_The_Feature_Configurator() {
        var services = new ServiceCollection();
        var configurators = new Configurators();
        configurators.Set<PushedAuthorizationRequestsOptions>(options => {
            options.Lifetime = TimeSpan.FromSeconds(90);
            options.RequireForAllClients = true;
            options.AllowRequestUriReplay = true;
        });

        new PushedAuthorizationRequestsFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);
        services.Configure(configurators.PopOrDefault<PushedAuthorizationRequestsOptions>());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PushedAuthorizationRequestsOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(90), options.Lifetime);
        Assert.True(options.RequireForAllClients);
        Assert.True(options.AllowRequestUriReplay);
    }

    [Fact]
    public void Apply_Jwt_Secured_Authorization_Request_Options_Through_The_Feature_Configurator() {
        var services = new ServiceCollection();
        var configurators = new Configurators();
        configurators.Set<JwtSecuredAuthorizationRequestsOptions>(options => {
            options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
            options.RequireForAllClients = true;
        });

        new JwtSecuredAuthorizationRequestsFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);
        services.Configure(configurators.PopOrDefault<JwtSecuredAuthorizationRequestsOptions>());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JwtSecuredAuthorizationRequestsOptions>>().Value;
        Assert.Contains(SigningAlgorithms.RsaSha256, options.SigningAlgorithms);
        Assert.True(options.RequireForAllClients);
    }

    [Fact]
    public void Apply_Native_Single_Sign_On_Options_Through_The_Feature_Configurator() {
        var services = new ServiceCollection();
        var configurators = new Configurators();
        configurators.Set<NativeSingleSignOnOptions>(options =>
            options.DeviceSecretLifetime = TimeSpan.FromDays(30));

        new NativeSingleSignOnFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);
        services.Configure(configurators.PopOrDefault<NativeSingleSignOnOptions>());

        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            TimeSpan.FromDays(30),
            provider.GetRequiredService<IOptions<NativeSingleSignOnOptions>>().Value.DeviceSecretLifetime);
    }

    [Fact]
    public void Apply_Session_Management_Options_Through_The_Feature_Configurator() {
        var services = new ServiceCollection();
        var configurators = new Configurators();
        configurators.Set<SessionManagementOptions>(options => options.OpStateCookieName = "custom.opstate");

        new SessionManagementFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);
        services.Configure(configurators.PopOrDefault<SessionManagementOptions>());

        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            "custom.opstate",
            provider.GetRequiredService<IOptions<SessionManagementOptions>>().Value.OpStateCookieName);
    }
}
