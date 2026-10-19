using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

public class HandledResponseShould
{
    private static readonly Student Entity = new() { Name = "s1", CanonicalName = "students/s1" };

    [Fact]
    public async Task Create_Request_Advisor_Handles_Through_The_Shared_Carrier() {
        var handled = new CreateResultBase<Student> { Detail = new() { CanonicalName = "students/new" } };
        var mutation = ResourceMutationMock.Create<Student>();
        var advisor = new Mock<IResourceCreateRequestAdvisor<Student, Student, Student>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<Student>(),
                          It.IsAny<ResourceRequestContainer<Student>>(),
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<HandledResponse<CreateResultBase<Student>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext _,
                         Student _,
                         ResourceRequestContainer<Student> _,
                         ClaimsPrincipal? _,
                         HandledResponse<CreateResultBase<Student>> carrier,
                         CancellationToken _) => {
                   ((IHandledResponse<CreateResultBase<Student>>)carrier).SetResponse(handled);
                   return Task.FromResult(AdviseResult.Handle);
               });
        using var services = Services(mutation.Object, advisor.Object);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, Mock.Of<IRepository<Student>>(MockBehavior.Strict), Mock.Of<ISimpleMapper>());

        var result = await handler.CreateAsync(new() { Name = "new" }, null, advice, CancellationToken.None);

        Assert.Same(handled, result);
        mutation.Verify(value => value.CreateAsync(
                            It.IsAny<Student>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Request_Advisor_Block_Prevents_The_Mutation() {
        var mutation = ResourceMutationMock.Create<Student>();
        var advisor = new Mock<IResourceCreateRequestAdvisor<Student, Student, Student>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<Student>(),
                          It.IsAny<ResourceRequestContainer<Student>>(),
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<HandledResponse<CreateResultBase<Student>>>(),
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Block);
        using var services = Services(mutation.Object, advisor.Object);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, Mock.Of<IRepository<Student>>(MockBehavior.Strict), Mock.Of<ISimpleMapper>());

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.CreateAsync(new() { Name = "new" }, null, advice, CancellationToken.None));

        mutation.Verify(value => value.CreateAsync(
                            It.IsAny<Student>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Advisor_Handles_Through_The_Shared_Carrier() {
        var handled = new UpdateResultBase<Student> { Detail = Entity };
        var mutation = ResourceMutationMock.Create<Student>();
        var advisor = new Mock<IResourceUpdateAdvisor<Student, Student, Student>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<Student>(),
                          It.IsAny<Student>(),
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<HandledResponse<UpdateResultBase<Student>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext _,
                         Student _,
                         Student _,
                         ClaimsPrincipal? _,
                         HandledResponse<UpdateResultBase<Student>> carrier,
                         CancellationToken _) => {
                   ((IHandledResponse<UpdateResultBase<Student>>)carrier).SetResponse(handled);
                   return Task.FromResult(AdviseResult.Handle);
               });
        using var services = Services(mutation.Object, update: advisor.Object);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, LoadedRepository().Object, Mock.Of<ISimpleMapper>());

        var result = await handler.UpdateAsync("students/s1", new() { FullName = "Renamed" }, null, advice, CancellationToken.None);

        Assert.Same(handled, result);
        mutation.Verify(value => value.UpdateAsync(
                            It.IsAny<Student>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(), 
                            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Request_Advisor_Continue_Reaches_The_Mutation() {
        var mutation = ResourceMutationMock.Create<Student>();
        var advisor = new Mock<IResourceUpdateRequestAdvisor<Student, Student, Student>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<Student>(),
                          It.IsAny<ResourceRequestContainer<Student>>(),
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<HandledResponse<UpdateResultBase<Student>>>(),
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Continue);
        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(value => value.Map<Student, Student>(Entity)).Returns(Entity);
        using var services = Services(mutation.Object, updateRequest: advisor.Object);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, LoadedRepository().Object, mapper.Object);

        var result = await handler.UpdateAsync("students/s1", new() { FullName = "Renamed" }, null, advice, CancellationToken.None);

        Assert.Same(Entity, result.Detail);
        mutation.Verify(value => value.UpdateAsync(Entity, null, Operations.Update, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Method_Request_Advisor_Handle_Returns_The_Written_Response() {
        var handled = new MethodResponse { CanonicalName = "methodEntities/handled" };
        var handler = new MethodHandler();
        var advisor = new Mock<IResourceMethodRequestAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>>(),
                          It.IsAny<ResourceRequestContainer<MethodEntity>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext _,
                         ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse> execution,
                         ResourceRequestContainer<MethodEntity> _,
                         CancellationToken _) => {
                   execution.SetResponse(handled);
                   return Task.FromResult(AdviseResult.Handle);
               });
        using var services = MethodServices(handler, advisor.Object);
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();

        var response = await operation.InvokeAsync("batchArchive", null, new MethodRequest(), null, default);

        Assert.Same(handled, response);
        Assert.Equal(0, handler.Invocations);
    }

    [Fact]
    public async Task Method_Request_Advisor_Block_Prevents_Dispatch() {
        var handler = new MethodHandler();
        var advisor = new Mock<IResourceMethodRequestAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>>(),
                          It.IsAny<ResourceRequestContainer<MethodEntity>>(),
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Block);
        using var services = MethodServices(handler, advisor.Object);
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();

        await Assert.ThrowsAsync<NotFoundException>(
            () => operation.InvokeAsync("batchArchive", null, new MethodRequest(), null, default));

        Assert.Equal(0, handler.Invocations);
    }

    [Fact]
    public async Task Method_Request_Advisor_Continue_Runs_Dispatch() {
        var handler = new MethodHandler();
        var advisor = new Mock<IResourceMethodRequestAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>>(),
                          It.IsAny<ResourceRequestContainer<MethodEntity>>(),
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Continue);
        using var services = MethodServices(handler, advisor.Object);
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();

        var response = await operation.InvokeAsync("batchArchive", null, new MethodRequest(), null, default);

        Assert.Same(handler.Response, response);
        Assert.Equal(1, handler.Invocations);
    }

    [Fact]
    public async Task Instance_Method_Stages_Share_One_Carrier() {
        ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>? requestStage = null;
        ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>? methodStage  = null;
        var request = new Mock<IResourceMethodRequestAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        request.SetupGet(value => value.Order).Returns(0);
        request.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>>(),
                          It.IsAny<ResourceRequestContainer<MethodEntity>>(),
                          It.IsAny<CancellationToken>()))
               .Callback((AdviceContext _,
                          ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse> execution,
                          ResourceRequestContainer<MethodEntity> _,
                          CancellationToken _) => requestStage = execution)
               .ReturnsAsync(AdviseResult.Continue);
        var method = new Mock<IResourceMethodAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        method.SetupGet(value => value.Order).Returns(0);
        method.Setup(value => value.AdviseAsync(
                         It.IsAny<AdviceContext>(),
                         It.IsAny<ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse>>(),
                         It.IsAny<CancellationToken>()))
              .Callback((AdviceContext _,
                         ResourceMethodExecution<MethodEntity, MethodRequest, MethodResponse> execution,
                         CancellationToken _) => methodStage = execution)
              .ReturnsAsync(AdviseResult.Continue);
        var entity = new MethodEntity { CanonicalName = "methodEntities/one" };
        var repository = new Mock<IRepository<MethodEntity>>();
        repository.Setup(value => value.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<MethodEntity>, IQueryable<MethodEntity>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(new ValueTask<MethodEntity?>(entity));
        using var services = MethodServices(new MethodHandler(), request.Object, method.Object, repository.Object);
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();

        await operation.InvokeAsync("archive", "methodEntities/one", new MethodRequest(), null, default);

        Assert.NotNull(requestStage);
        Assert.Same(requestStage, methodStage);
    }

    [Fact]
    public async Task Stage_Continue_Returns_No_Handled_Response() {
        var carrier = new HandledResponse<DeleteResultBase<Student>>();

        var result = await HandledResponseRunner.RunAsync(
            carrier, () => Task.FromResult(AdviseResult.Continue), Blocked);

        Assert.Null(result);
    }

    [Fact]
    public async Task Stage_Handle_Without_A_Response_Raises_The_Blocked_Exception() {
        var carrier = new HandledResponse<DeleteResultBase<Student>>();

        await Assert.ThrowsAsync<NotSupportedException>(() => HandledResponseRunner.RunAsync(
            carrier, () => Task.FromResult(AdviseResult.Handle), Blocked));
    }

    [Fact]
    public async Task Stage_Handle_Without_A_Response_Returns_The_Delete_Fallback() {
        var carrier  = new HandledResponse<DeleteResultBase<Student>>();
        var fallback = new DeleteResultBase<Student>();

        var result = await HandledResponseRunner.RunAsync(
            carrier, () => Task.FromResult(AdviseResult.Handle), Blocked, () => fallback);

        Assert.Same(fallback, result);
    }

    [Fact]
    public async Task Update_Missing_Resource_Projects_A_Handled_Create_Response() {
        var detail  = new Student { CanonicalName = "students/missing" };
        var handled = new CreateResultBase<Student> { Detail = detail };
        var create = new Mock<IResourceCreateRequestAdvisor<Student, Student, Student>>();
        create.SetupGet(value => value.Order).Returns(0);
        create.Setup(value => value.AdviseAsync(
                         It.IsAny<AdviceContext>(),
                         It.IsAny<Student>(),
                         It.IsAny<ResourceRequestContainer<Student>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<HandledResponse<CreateResultBase<Student>>>(),
                         It.IsAny<CancellationToken>()))
              .Returns((AdviceContext _,
                        Student _,
                        ResourceRequestContainer<Student> _,
                        ClaimsPrincipal? _,
                        HandledResponse<CreateResultBase<Student>> carrier,
                        CancellationToken _) => {
                  carrier.SetResponse(handled);
                  return Task.FromResult(AdviseResult.Handle);
              });
        var update = new Mock<IResourceUpdateRequestAdvisor<Student, Student, Student>>();
        update.SetupGet(value => value.Order).Returns(0);
        update.Setup(value => value.AdviseAsync(
                         It.IsAny<AdviceContext>(),
                         It.IsAny<Student>(),
                         It.IsAny<ResourceRequestContainer<Student>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<HandledResponse<UpdateResultBase<Student>>>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(AdviseResult.Continue);
        var mutation = ResourceMutationMock.Create<Student>();
        using var services = Services(mutation.Object, create.Object, update.Object);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, MissingRepository(), Mock.Of<ISimpleMapper>());

        var result = await handler.UpdateAsync(
            "students/missing", new() { Name = "missing", AllowMissing = true }, null, advice, CancellationToken.None);

        Assert.Same(detail, result.Detail);
        mutation.Verify(value => value.CreateAsync(
                            It.IsAny<Student>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Exception Blocked() { return new NotSupportedException("blocked"); }

    private static ServiceProvider Services(
        IResourceMutation<Student>                             mutation,
        IResourceCreateRequestAdvisor<Student, Student, Student>? createRequest  = null,
        IResourceUpdateRequestAdvisor<Student, Student, Student>? updateRequest  = null,
        IResourceUpdateAdvisor<Student, Student, Student>?        update         = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(mutation);
        if (createRequest is not null) services.AddSingleton(createRequest);
        if (updateRequest is not null) services.AddSingleton(updateRequest);
        if (update is not null) services.AddSingleton(update);
        return services.BuildServiceProvider();
    }

    private static IRepository<Student> MissingRepository() {
        var repository = new Mock<IRepository<Student>>();
        repository.Setup(value => value.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(new ValueTask<Student?>((Student?)null));
        return repository.Object;
    }

    private static Mock<IRepository<Student>> LoadedRepository() {
        var repository = new Mock<IRepository<Student>>();
        repository.Setup(value => value.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(new ValueTask<Student?>(Entity));
        return repository;
    }

    private static ServiceProvider MethodServices(
        MethodHandler                                                     handler,
        IResourceMethodRequestAdvisor<MethodEntity, MethodRequest, MethodResponse>? requestAdvisor = null,
        IResourceMethodAdvisor<MethodEntity, MethodRequest, MethodResponse>?        methodAdvisor  = null,
        IRepository<MethodEntity>?                                                 repository     = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(repository ?? Mock.Of<IRepository<MethodEntity>>(MockBehavior.Strict));
        services.AddSingleton(Registry());
        services.AddSingleton<IRequestHandler<MethodRequest, MethodResponse>>(handler);
        services.AddSingleton(provider => new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            provider.GetRequiredService<IRepository<MethodEntity>>(), provider, new InProcessRequestDispatcher(provider)));
        services.AddSingleton<
            IRequestHandler<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>,
            ResourceMethodDispatchHandler<MethodEntity, MethodRequest, MethodResponse>>();
        if (requestAdvisor is not null) services.AddSingleton(requestAdvisor);
        if (methodAdvisor is not null) services.AddSingleton(methodAdvisor);
        return services.BuildServiceProvider();
    }

    private static ResourceRegistry Registry() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute(typeof(MethodEntity)), [
            new ResourceMethodAttribute("archive", typeof(MethodHandler)),
            new ResourceMethodAttribute("batchArchive", typeof(MethodHandler), ResourceMethodScope.Collection),
        ]);
        return registry;
    }

    [CanonicalName("methodEntities/{methodEntity}")]
    public sealed class MethodEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class MethodRequest : ICanonicalName, ICommand<MethodResponse>, IRequestPrincipal
    {
        public string?          Name          { get; set; }
        public string?          CanonicalName { get; set; }
        public ClaimsPrincipal? Principal     { get; set; }
    }

    public sealed class MethodResponse : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class MethodHandler : IRequestHandler<MethodRequest, MethodResponse>
    {
        public int Invocations { get; private set; }

        public MethodResponse Response { get; } = new();

        public Task<MethodResponse> HandleAsync(MethodRequest request, AdviceContext context, CancellationToken ct = default) {
            Invocations++;
            return Task.FromResult(Response);
        }
    }
}
