using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Http;
using Schemata.Resource.Tests.Fixtures;
using System.Text.Json;
using Xunit;

namespace Schemata.Resource.Tests.Http;

public class ResourceControllerNoWriteShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Create_NoWrite_RespondsNoContent_WithoutLocationOrBody() {
        var dispatcher = DispatcherReturningEmptyCreate();
        using var provider = new ServiceCollection().AddSingleton(dispatcher.Object).BuildServiceProvider();
        var controller  = Controller(provider);

        var result = await controller.CreateAsync(new() { Name = "s1" });

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(controller.HttpContext.Response.Headers.Location.ToString());
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Update_NoWrite_RespondsNoContent() {
        var dispatcher = DispatcherReturningEmptyUpdate();
        using var provider = new ServiceCollection().AddSingleton(dispatcher.Object).BuildServiceProvider();
        var controller  = Controller(provider);

        var result = await controller.UpdateAsync("s1", new() { Name = "s1" });

        Assert.IsType<NoContentResult>(result);
    }

    private static Mock<IRequestDispatcher> DispatcherReturningEmptyCreate() {
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(d => d.SendAsync<CreateResourceRequest<Student, Student, Student>, CreateResultBase<Student>>(
                          It.IsAny<CreateResourceRequest<Student, Student, Student>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CreateResultBase<Student>());
        return dispatcher;
    }

    private static Mock<IRequestDispatcher> DispatcherReturningEmptyUpdate() {
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(d => d.SendAsync<UpdateResourceRequest<Student, Student, Student>, UpdateResultBase<Student>>(
                          It.IsAny<UpdateResourceRequest<Student, Student, Student>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new UpdateResultBase<Student>());
        return dispatcher;
    }

    private static ResourceController<Student, Student, Student, Student> Controller(IServiceProvider provider) {
        var controller = new ResourceController<Student, Student, Student, Student>(
            provider, Options.Create(new JsonSerializerOptions())) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };

        return controller;
    }
}
