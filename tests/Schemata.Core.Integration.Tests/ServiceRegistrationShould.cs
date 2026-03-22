using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Schemata.Core.Integration.Tests;

[Trait("Category", "Integration")]
public class ServiceRegistrationShould
{
    [Fact]
    public void UseSchemata_ConfigureAction_ApplyToOptions() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(_ => { }, opts => { opts.Set("test-key", "test-value"); });

        var app     = builder.Build();
        var options = app.Services.GetRequiredService<SchemataOptions>();
        var value   = options.Get<string>("test-key");

        Assert.Equal("test-value", value);
    }
}
