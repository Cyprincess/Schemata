using Microsoft.AspNetCore.Hosting;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

public sealed class ParticipantWebAppFactory : GrpcWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ParticipantSecurity", "true");
    }
}
