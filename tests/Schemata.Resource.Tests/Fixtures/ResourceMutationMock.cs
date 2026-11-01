using System.Threading;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Tests.Fixtures;

/// <summary>Builds a Moq <see cref="IResourceMutation{TEntity}" /> double whose writes all apply.</summary>
public static class ResourceMutationMock
{
    public static Mock<IResourceMutation<TEntity>> Create<TEntity>()
        where TEntity : class, ICanonicalName {
        var mutation = new Mock<IResourceMutation<TEntity>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<TEntity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.UpdateAsync(
                    It.IsAny<TEntity>(),
                    It.IsAny<IUnitOfWork?>(),
                    It.IsAny<Operations>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.DeleteAsync(
                    It.IsAny<TEntity>(),
                    It.IsAny<IUnitOfWork?>(),
                    It.IsAny<Operations>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        return mutation;
    }
}
