using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Transport.RabbitMq.Runtime;
using Xunit;

namespace Schemata.Transport.RabbitMq.Tests;

public class RabbitMqConnectionProviderShould
{
    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Disposed_Provider_Rejects_Connection_Acquisition_Without_Connecting() {
        var provider = new RabbitMqConnectionProvider(Options.Create(new RabbitMqConnectionOptions()));
        await provider.DisposeAsync();
        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.GetConnectionAsync());
    }
}
