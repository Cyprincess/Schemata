using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc;
using Schemata.Resource.Tests.Fixtures;
using Xunit;
using ErrorCodes = Schemata.Abstractions.SchemataConstants.ErrorCodes;

namespace Schemata.Resource.Tests;

public class ResourceServiceBoundaryShould
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UpdateWithoutCanonicalName_ThrowsInvalidArgument(string? name) {
        using var services = new ServiceCollection().BuildServiceProvider();
        var service = new ResourceService<Student, Student, Student, Student>(
            services, new Mock<IHttpContextAccessor>().Object);

        var ex = await Assert.ThrowsAsync<InvalidArgumentException>(
            () => service.UpdateAsync(new Student { CanonicalName = name }).AsTask());

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DeleteWithoutCanonicalName_ThrowsInvalidArgument(string? name) {
        using var services = new ServiceCollection().BuildServiceProvider();
        var service = new ResourceService<Student, Student, Student, Student>(
            services, new Mock<IHttpContextAccessor>().Object);

        var ex = await Assert.ThrowsAsync<InvalidArgumentException>(
            () => service.DeleteAsync(new DeleteRequest { CanonicalName = name }).AsTask());

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Status);
    }
}
