using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Event.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Event.Integration.Tests;

[Trait("Category", "Integration")]
public class EventAuditWithoutNameAdvisorShould
{
    [Fact]
    public async Task Publish_Without_Name_Advisor_Swallows_Validation_And_Persists_No_Row() {
        var fixture = new EventAuditFixture(withNameAdvisor: false);
        await fixture.InitializeAsync();

        try {
            await fixture.PublishAsync(new StudentCreated("alice"));

            Assert.Equal(0, await fixture.CountAsync());

            fixture.BusLogger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("IEventLifecycleObserver.OnPublishedAsync threw for event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        } finally {
            await fixture.DisposeAsync();
        }
    }
}
