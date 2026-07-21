using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class ResourceMethodOperationHandlerShould
{
    [Fact]
    public async Task CollectionMethod_InvokesHandlerWithoutLoadingEntity() {
        var repository = Mock.Of<Entity.Repository.IRepository<MethodEntity>>(MockBehavior.Strict);
        using var services = Services();
        var operation = new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            repository, services);
        MethodEntity? invokedEntity = null;
        var handler = new Mock<IResourceMethodHandler<MethodEntity, MethodRequest, MethodResponse>>();
        handler.Setup(h => h.InvokeAsync(
                   It.IsAny<string?>(),
                   It.IsAny<MethodRequest>(),
                   It.IsAny<MethodEntity?>(),
                   It.IsAny<ClaimsPrincipal?>(),
                   It.IsAny<CancellationToken>()))
               .Callback((string? name, MethodRequest request, MethodEntity? entity, ClaimsPrincipal? principal, CancellationToken cancellationToken) => invokedEntity = entity)
               .Returns(ValueTask.FromResult(new MethodResponse()));

        await operation.InvokeAsync(handler.Object, "batchArchive", null, new(), null, CancellationToken.None);

        Assert.Null(invokedEntity);
    }

    private static ServiceProvider Services() {
        return new ServiceCollection().BuildServiceProvider();
    }

    [CanonicalName("methodEntities/{methodEntity}")]
    public sealed class MethodEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class MethodRequest : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class MethodResponse : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

}
