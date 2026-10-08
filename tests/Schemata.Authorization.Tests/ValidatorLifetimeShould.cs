using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Caching.Skeleton;
using Schemata.Core;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ValidatorLifetimeShould
{
    [Fact]
    public void Resolve_The_Proof_And_Assertion_Validators_Through_Their_Real_Consumers_In_A_Validated_Scope() {
        using var provider = Provider();
        using var scope    = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<DPopProofValidator>();
        scope.ServiceProvider.GetRequiredService<ClientAssertionValidator>();

        Assert.Contains(
            scope.ServiceProvider.GetRequiredService<IEnumerable<ITokenRequestAdvisor<SchemataApplication>>>(),
            advisor => advisor is AdviceRequestDpop<SchemataApplication>);
        Assert.Contains(
            scope.ServiceProvider.GetRequiredService<IEnumerable<IClientAuthentication<SchemataApplication>>>(),
            authentication => authentication is ClientSecretJwtAuthentication<SchemataApplication>);
        Assert.Contains(
            scope.ServiceProvider.GetRequiredService<IEnumerable<IClientAuthentication<SchemataApplication>>>(),
            authentication => authentication is PrivateKeyJwtAuthentication<SchemataApplication>);
    }

    [Fact]
    public void Give_Each_Scope_Its_Own_Validator_And_Reject_Root_Resolution() {
        using var provider = Provider();
        using var first    = provider.CreateScope();
        using var second   = provider.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<DPopProofValidator>(),
            second.ServiceProvider.GetRequiredService<DPopProofValidator>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ClientAssertionValidator>(),
            second.ServiceProvider.GetRequiredService<ClientAssertionValidator>());

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<DPopProofValidator>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ClientAssertionValidator>());
    }

    private static ServiceProvider Provider() {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(Mock.Of<ICacheProvider>());
        services.AddSingleton(Mock.Of<IApplicationManager<SchemataApplication>>());
        services.AddSingleton(Mock.Of<ISecurityStore<SchemataSecurity>>());
        services.AddSingleton(Mock.Of<IHttpClientFactory>());
        services.AddKeyedScoped(SecurityConstants.TokenTypes.Nonce, (_, _) => Mock.Of<ITokenStore<SchemataToken>>());
        services.AddKeyedScoped(SecurityConstants.TokenTypes.Jti, (_, _) => Mock.Of<ITokenStore<SchemataToken>>());

        var configurators = new Configurators();
        new DemonstratingProofOfPossessionFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);
        new ClientAssertionAuthenticationFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);
        new JwtBearerGrantFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
