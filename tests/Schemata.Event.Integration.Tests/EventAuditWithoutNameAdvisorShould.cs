using System;
using System.Threading.Tasks;
using Schemata.Event.Integration.Tests.Fixtures;
using Schemata.Abstractions.Exceptions;
using Xunit;

namespace Schemata.Event.Integration.Tests;

[Trait("Category", "Integration")]
public class EventAuditWithoutNameAdvisorShould
{
    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Publish_Without_Name_Advisor_Propagates_Validation_And_Persists_No_Row() {
        var fixture = new EventAuditFixture(withNameAdvisor: false);
        await fixture.InitializeAsync();

        try {
            await Assert.ThrowsAsync<ValidationException>(() => fixture.PublishAsync(new StudentCreated("alice")));

            Assert.Equal(0, await fixture.CountAsync());
        } finally {
            await fixture.DisposeAsync();
        }
    }
}
