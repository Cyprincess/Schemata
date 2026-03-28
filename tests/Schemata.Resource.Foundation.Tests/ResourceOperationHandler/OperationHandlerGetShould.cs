using System.Threading.Tasks;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.ResourceOperationHandler;

public class OperationHandlerGetShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Get_ExistingEntity_ReturnsAllowed() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];

        var result = await handler.GetAsync(entity.CanonicalName!, null, null);

        Assert.NotNull(result);
        Assert.True(result.IsAllowed());
    }
}
