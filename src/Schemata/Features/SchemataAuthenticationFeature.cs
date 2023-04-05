using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public class SchemataAuthenticationFeature : FeatureBase
{
    public override int Priority => 170_000_000;

    private readonly Action<AuthenticationBuilder> _build;
    private readonly Action<AuthenticationOptions> _authenticate;
    private readonly Action<AuthorizationOptions>  _authorize;

    public SchemataAuthenticationFeature(
        Action<AuthenticationBuilder> build,
        Action<AuthenticationOptions> authenticate,
        Action<AuthorizationOptions>  authorize) {
        _build        = build;
        _authenticate = authenticate;
        _authorize    = authorize;
    }

    public override void ConfigureServices(IServiceCollection services, IConfiguration conf, IWebHostEnvironment env) {
        var builder = services.AddAuthentication(_authenticate);
        _build(builder);
        services.AddAuthorization(_authorize);
    }

    public override void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
