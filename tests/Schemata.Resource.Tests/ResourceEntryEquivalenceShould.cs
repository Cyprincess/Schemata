using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Resource.Http;
using Xunit;

namespace Schemata.Resource.Tests;

/// <summary>
///     Proves the production HTTP entry (<see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" />,
///     driven with a fabricated <see cref="ControllerContext" /> exactly as ASP.NET Core would construct
///     one per request) and a raw <see cref="IRequestDispatcher" /> entry run the exact same Create/Get
///     pipeline: equal results, the registered <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" />
///     firing once per entry, and identical exception payloads.
///     Both entries resolve <see cref="IRequestDispatcher" /> from the same
///     <see cref="AddSchemataResources" />-configured container — neither entry stubs the real
///     <see cref="DefaultCreateResourceHandler{TEntity,TRequest,TDetail,TSummary}" /> /
///     <see cref="DefaultGetResourceHandler{TEntity,TRequest,TDetail,TSummary}" /> handlers, the real
///     <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" /> pipeline, or the
///     recording advisors, which are registered in DI and observed firing through the real dispatch —
///     not invoked manually.
/// </summary>
public sealed class ResourceEntryEquivalenceShould
{
    [Fact]
    public async Task Create_Through_Controller_And_Dispatcher_Produce_Equal_Details_And_Fire_The_Same_Advisor() {
        var controllerSpy = new RecordingCommandAdvisor();
        using var controllerServices = BuildServices(CreatePipelineDoubles("e1"), commandAdvisor: controllerSpy);
        using var controllerScope = controllerServices.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var controller = BuildController(controllerScope.ServiceProvider, principal);

        var controllerResult = await controller.CreateAsync(new Request { Name = "e1" });
        var controllerDetail = Assert.IsType<Detail>(Assert.IsType<JsonResult>(controllerResult).Value);

        var dispatcherSpy = new RecordingCommandAdvisor();
        using var dispatcherServices = BuildServices(CreatePipelineDoubles("e1"), commandAdvisor: dispatcherSpy);
        using var dispatcherScope = dispatcherServices.CreateScope();
        var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var dispatcherResult = await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(new Request { Name = "e1" }, principal), CancellationToken.None);

        Assert.Equal(controllerDetail.Name, dispatcherResult.Detail!.Name);
        Assert.Equal(controllerDetail.CanonicalName, dispatcherResult.Detail!.CanonicalName);
        Assert.Equal(1, controllerSpy.Count);
        Assert.Equal(1, dispatcherSpy.Count);
    }

    [Fact]
    public async Task Get_Through_Controller_And_Dispatcher_Produce_Equal_Details_And_Fire_The_Same_Advisor() {
        var controllerSpy = new RecordingQueryAdvisor();
        using var controllerServices = BuildServices(CreateGetDoubles("entities/e1", found: true), queryAdvisor: controllerSpy);
        using var controllerScope = controllerServices.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var controller = BuildController(controllerScope.ServiceProvider, principal);

        var controllerResult = await controller.GetAsync("e1");
        var controllerDetail = Assert.IsType<Detail>(Assert.IsType<JsonResult>(controllerResult).Value);

        var dispatcherSpy = new RecordingQueryAdvisor();
        using var dispatcherServices = BuildServices(CreateGetDoubles("entities/e1", found: true), queryAdvisor: dispatcherSpy);
        using var dispatcherScope = dispatcherServices.CreateScope();
        var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var dispatcherResult = await dispatcher.SendAsync<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>(
            new(new GetRequest { CanonicalName = "entities/e1" }, principal), CancellationToken.None);

        Assert.Equal(controllerDetail.Name, dispatcherResult.Detail!.Name);
        Assert.Equal(controllerDetail.CanonicalName, dispatcherResult.Detail!.CanonicalName);
        Assert.Equal(1, controllerSpy.Count);
        Assert.Equal(1, dispatcherSpy.Count);
    }

    [Fact]
    public async Task Get_Throw_The_Same_Exception_Payload_Through_Both_Entries_For_A_Missing_Resource() {
        using var controllerServices = BuildServices(CreateGetDoubles("entities/missing", found: false));
        using var controllerScope = controllerServices.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var controller = BuildController(controllerScope.ServiceProvider, principal);
        var controllerException = await Record.ExceptionAsync(() => controller.GetAsync("missing"));

        using var dispatcherServices = BuildServices(CreateGetDoubles("entities/missing", found: false));
        using var dispatcherScope = dispatcherServices.CreateScope();
        var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var dispatcherException = await Record.ExceptionAsync(() => dispatcher.SendAsync<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>(
            new(new GetRequest { CanonicalName = "entities/missing" }, principal), CancellationToken.None));

        var controllerNotFound = Assert.IsType<NotFoundException>(controllerException);
        var dispatcherNotFound = Assert.IsType<NotFoundException>(dispatcherException);

        Assert.Equal(controllerNotFound.Code, dispatcherNotFound.Code);
        Assert.Equal(controllerNotFound.Status, dispatcherNotFound.Status);
        Assert.Equal(controllerNotFound.Message, dispatcherNotFound.Message);
        Assert.Equal(ErrorInfo(controllerNotFound).Reason, ErrorInfo(dispatcherNotFound).Reason);
        Assert.Equal(ErrorInfo(controllerNotFound).Metadata, ErrorInfo(dispatcherNotFound).Metadata);
    }

    private static ErrorInfoDetail ErrorInfo(SchemataException exception) {
        return exception.Details!.OfType<ErrorInfoDetail>().Single();
    }

    /// <summary>Builds a controller wired exactly as ASP.NET Core would: a fabricated per-request
    /// <see cref="DefaultHttpContext" />/<see cref="ControllerContext" /> carrying <paramref name="principal" />
    /// as <c>HttpContext.User</c>, and a mocked <see cref="IUrlHelper" /> so <c>Url.Action(...)</c> in
    /// <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}.CreateAsync" /> does not require a
    /// real routing host.</summary>
    private static ResourceController<Entity, Request, Detail, Summary> BuildController(
        IServiceProvider services,
        ClaimsPrincipal  principal
    ) {
        var httpContext = new DefaultHttpContext { RequestServices = services, User = principal };
        var controller = new ResourceController<Entity, Request, Detail, Summary>(services, Options.Create(new JsonSerializerOptions())) {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("http://localhost/entities/e1");
        controller.Url = urlHelper.Object;

        return controller;
    }

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreatePipelineDoubles(string name) {
        var entity = new Entity { Name = name, CanonicalName = $"entities/{name}" };
        var detail = new Detail { Name = name, CanonicalName = $"entities/{name}" };

        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>())).Returns(entity);
        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>())).Returns(detail);

        return (repository, mapper);
    }

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreateGetDoubles(string name, bool found) {
        var entity = found ? new Entity { Name = name, CanonicalName = name } : null;
        var detail = found ? new Detail { Name = name, CanonicalName = name } : null;

        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(), It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<Entity?>(entity));

        var mapper = new Mock<ISimpleMapper>();
        if (entity is not null) {
            mapper.Setup(m => m.Map<Entity, Detail>(entity)).Returns(detail!);
        }

        return (repository, mapper);
    }

    private static ServiceProvider BuildServices(
        (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper)      doubles,
        IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>? commandAdvisor = null,
        IRequestPipelineAdvisor<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>?          queryAdvisor   = null
    ) {
        var services = new ServiceCollection();
        services.AddSchemataResources();
        services.AddSingleton(Mock.Of<ICacheProvider>());
        services.AddSingleton(doubles.Repository.Object);
        services.AddSingleton(doubles.Mapper.Object);
        services.AddScoped<
            IRequestHandler<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>,
            DefaultCreateResourceHandler<Entity, Request, Detail, Summary>>();
        services.AddScoped<
            IRequestHandler<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>,
            DefaultGetResourceHandler<Entity, Request, Detail, Summary>>();
        if (commandAdvisor is not null) {
            services.AddSingleton(commandAdvisor);
        }

        if (queryAdvisor is not null) {
            services.AddSingleton(queryAdvisor);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Records every dispatch of <see cref="CreateResourceRequest{TEntity,TRequest,TDetail}" /> it observes.</summary>
    private sealed class RecordingCommandAdvisor : IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<CreateResultBase<Detail>> AdviseAsync(
            AdviceContext                                              ctx,
            CreateResourceRequest<Entity, Request, Detail>             a1,
            RequestHandlerContinuation<CreateResultBase<Detail>>       next,
            CancellationToken                                          ct = default
        ) {
            Count++;
            return next(ct);
        }
    }

    /// <summary>Records every dispatch of <see cref="GetResourceQueryRequest{TEntity,TDetail}" /> it observes.</summary>
    private sealed class RecordingQueryAdvisor : IRequestPipelineAdvisor<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<GetResultBase<Detail>> AdviseAsync(
            AdviceContext                                    ctx,
            GetResourceQueryRequest<Entity, Detail>          a1,
            RequestHandlerContinuation<GetResultBase<Detail>> next,
            CancellationToken                                ct = default
        ) {
            Count++;
            return next(ct);
        }
    }

    [CanonicalName("entities/{entity}")]
    public sealed class Entity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Detail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
