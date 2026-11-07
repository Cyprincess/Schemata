using System;
using System.IO;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Core;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configure;
    private readonly string _environment;

    public const string InteractionScheme = "InteractionTest";

    public WebAppFactory() : this("Testing", null) { }

    private WebAppFactory(string environment, Action<IServiceCollection>? configure) {
        _environment = environment;
        _configure = configure;
    }

    public WebAppFactory WithEnvironment(string environment) { return new(environment, _configure); }

    public WebAppFactory WithServices(Action<IServiceCollection> configure) { return new(_environment, _configure + configure); }

    public WebAppFactory WithHistoricalFlows() {
        return WithServices(services => {
            var schemata      = new SchemataOptions();
            var configurators = new Configurators();
            new ImplicitFlowFeature().ConfigureServices(services, schemata, configurators);
            new HybridFlowFeature().ConfigureServices(services, schemata, configurators);
            new NoneFlowFeature().ConfigureServices(services, schemata, configurators);
        });
    }

    public WebAppFactory WithAuthenticatedInteraction() {
        return WithServices(services => services.AddAuthentication(InteractionScheme)
                                                .AddScheme<AuthenticationSchemeOptions, InteractionHandler>(InteractionScheme, null));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(GetProjectDirectory());
        if (_configure is not null) {
            builder.ConfigureServices(_configure);
        }
    }

    public sealed class InteractionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != InteractionScheme) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
            ], InteractionScheme));
            return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
        }
    }

    private static string GetProjectDirectory() {
        var dir = Path.GetDirectoryName(typeof(WebAppFactory).Assembly.Location)!;
        while (!File.Exists(Path.Combine(dir, "Schemata.Authorization.Integration.Tests.csproj"))
            && dir != Path.GetPathRoot(dir)) {
            dir = Path.GetDirectoryName(dir)!;
        }

        return dir;
    }
}
