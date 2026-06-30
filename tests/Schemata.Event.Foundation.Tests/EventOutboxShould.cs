using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class EventOutboxShould
{
    [Fact]
    public async Task PublishFails_RowStaysPending() {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(records.Object).BuildServiceProvider();
        var observer = new SchemataEventAuditObserver(services, Options.Create(new JsonSerializerOptions()));
        var context = new EventContext(Mock.Of<IEvent>(), "sample") {
            Payload = "{}", CorrelationId = "c1", RequiresOutboxDelivery = true,
        };

        // OnPublished records the outbox row; broker delivery controls the terminal callback.
        await observer.OnPublishedAsync(context);

        Assert.NotNull(context.Record);
        Assert.Equal(EventState.Pending, context.Record!.State);
    }
}
