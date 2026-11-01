using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.Features;
using Xunit;

namespace Schemata.Core.Tests;

[Trait("Layer", "Unit")]
public class AuthenticationFeatureShould
{
    [Fact]
    public void Register_The_Native_Authentication_Service_When_No_Component_Replaces_It() {
        var services = new ServiceCollection();
        new SchemataAuthenticationFeature().ConfigureServices(
            services,
            new SchemataOptions(),
            new Configurators(),
            new ConfigurationBuilder().Build(),
            null!);
        using var provider = services.BuildServiceProvider();

        // Exactly the native registration from AddAuthenticationCore: a component that needs
        // scheme-scope routing installs its own facade instead of Core doing it.
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationService));
        Assert.StartsWith(
            "Microsoft.AspNetCore.Authentication",
            provider.GetRequiredService<IAuthenticationService>().GetType().Assembly.GetName().Name!);
    }
}
