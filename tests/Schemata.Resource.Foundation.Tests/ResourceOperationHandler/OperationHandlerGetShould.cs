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

        var result = await handler.GetAsync(entity, null, null);

        Assert.NotNull(result);
        Assert.True(result.IsAllowed());
    }

    [Fact]
    public async Task FindByName_ExistingName_ReturnsEntity() {
        var handler = _fixture.CreateHandler();

        var entity = await handler.FindByNameAsync("students/alice-1", null);

        Assert.NotNull(entity);
        Assert.Equal("Alice", entity.FullName);
    }

    [Fact]
    public async Task FindByName_NonExistentName_ReturnsNull() {
        var handler = _fixture.CreateHandler();

        var entity = await handler.FindByNameAsync("students/nobody-999", null);

        Assert.Null(entity);
    }
}
