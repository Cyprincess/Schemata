using System.Threading.Tasks;
using Schemata.Event.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Event.Integration.Tests;

[Trait("Category", "Integration")]
public class EventAuditNameAdvisorShould
{
    [Fact]
    public async Task Publish_Persists_Audit_Row_With_Canonical_Name_From_EventType() {
        var fixture = new EventAuditFixture(withNameAdvisor: true);
        await fixture.InitializeAsync();

        try {
            await fixture.PublishAsync(new StudentCreated("alice"));

            Assert.Equal(1, await fixture.CountAsync());

            var row = await fixture.SingleOrDefaultAsync();

            Assert.NotNull(row);
            Assert.Equal("students/student-created", row!.EventType);
            Assert.Equal("students/student-created", row.Name);
            Assert.Equal("events/students/student-created", row.CanonicalName);
            Assert.NotEqual(default, row.Uid);
        } finally {
            await fixture.DisposeAsync();
        }
    }
}
