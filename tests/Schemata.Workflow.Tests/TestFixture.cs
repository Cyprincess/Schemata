using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Tests;

public class TestFixture
{
    public TestFixture() {
        Orders = [
            new() {
                Id        = 1,
                State     = nameof(OrderStateMachine.Initial),
                Timestamp = Guid.NewGuid(),
            },
            new() {
                Id        = 2,
                State     = nameof(OrderStateMachine.Initial),
                Timestamp = Guid.NewGuid(),
            },
        ];

        Transitions = [];

        Workflows = [
            new() {
                Id           = 1,
                InstanceType = typeof(Order).FullName!,
                InstanceId   = 1,
            },
            new() {
                Id           = 2,
                InstanceType = typeof(Order).FullName!,
                InstanceId   = 2,
            },
        ];

        OrderRepository.As<IRepository>()
                       .Setup(r => r.SingleOrDefaultAsync(It.IsAny<Expression<Func<IStatefulEntity, bool>>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((Expression<Func<IStatefulEntity, bool>> predicate, CancellationToken _) => Orders.AsQueryable().SingleOrDefault(predicate))
                       .Verifiable();

        OrderRepository.As<IRepository>()
                       .Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
                       .Returns((Order entity, CancellationToken _) => {
                            Orders.Add(entity);

                            return Task.CompletedTask;
                        })
                       .Verifiable();

        OrderRepository.As<IRepository>()
                       .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                       .Returns((CancellationToken _) => ValueTask.FromResult(0))
                       .Verifiable();

        TransitionRepository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataTransition>, IQueryable<SchemataTransition>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<SchemataTransition>, IQueryable<SchemataTransition>> predicate, CancellationToken ct) => List(Transitions.AsQueryable(), predicate, ct))
           .Verifiable();

        WorkflowRepository
           .Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataWorkflow>, IQueryable<SchemataWorkflow>>>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((Func<IQueryable<SchemataWorkflow>, IQueryable<SchemataWorkflow>> predicate, CancellationToken _) => predicate(Workflows.AsQueryable()).SingleOrDefault())
           .Verifiable();

        WorkflowRepository.Setup(r => r.AddAsync(It.IsAny<SchemataWorkflow>(), It.IsAny<CancellationToken>()))
                          .Returns((SchemataWorkflow entity, CancellationToken _) => {
                               Workflows.Add(entity);

                               return Task.CompletedTask;
                           })
                          .Verifiable();

        WorkflowRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                          .Returns((CancellationToken _) => ValueTask.FromResult(0))
                          .Verifiable();

        var builder = WebApplication.CreateBuilder()
                                    .UseSchemata(schema => {
                                         schema.UseMapster();

                                         schema.UseWorkflow()
                                               .Use<OrderStateMachine, Order>();

                                         schema.Services.AddTransient<IRepository<Order>>(_ => OrderRepository.Object);
                                         schema.Services.AddTransient<IRepository<SchemataTransition>>(_ => TransitionRepository.Object);
                                         schema.Services.AddTransient<IRepository<SchemataWorkflow>>(_ => WorkflowRepository.Object);
                                         schema.Services.AddScoped<WorkflowController>();
                                     });

        var app = builder.Build();

        var scope = app.Services.CreateScope();

        ServiceProvider = scope.ServiceProvider;
    }

    public IServiceProvider ServiceProvider { get; }

    public Mock<IRepository<Order>> OrderRepository { get; } = new Mock<IRepository>(MockBehavior.Strict).As<IRepository<Order>>();

    public Mock<IRepository<SchemataTransition>> TransitionRepository { get; } = new(MockBehavior.Strict);

    public Mock<IRepository<SchemataWorkflow>> WorkflowRepository { get; } = new(MockBehavior.Strict);

    public List<Order> Orders { get; }

    public List<SchemataTransition> Transitions { get; }

    public List<SchemataWorkflow> Workflows { get; }

    public (WorkflowController, MemoryStream) CreateWorkflowController() {
        var controller = ServiceProvider.GetRequiredService<WorkflowController>();

        var body = new MemoryStream();
        var context = new DefaultHttpContext {
            RequestServices = ServiceProvider,
            Response        = { Body = body },
        };

        controller.ControllerContext = new() {
            HttpContext = context,
        };

        return (controller, body);
    }

    private static async IAsyncEnumerable<T> List<T>(
        IQueryable<T>                              entities,
        Func<IQueryable<T>, IQueryable<T>>         predicate,
        [EnumeratorCancellation] CancellationToken ct) {
        foreach (var entity in predicate(entities.AsQueryable())) {
            yield return await Task.FromResult(entity);
        }
    }
}
