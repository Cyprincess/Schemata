using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Http.Tests;

public class TestFixture
{
    public TestFixture() {
        Students = [
            new() {
                Id        = 1,
                Name      = "Alice",
                Age       = 18,
                Grade     = 1,
                Timestamp = Guid.NewGuid(),
            },
            new() {
                Id        = 2,
                Name      = "Bob",
                Age       = 19,
                Grade     = 2,
                Timestamp = Guid.NewGuid(),
            },
        ];

        Repository.Setup(r => r.Once()).Returns(() => Repository.Object).Verifiable();
        Repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(() => Repository.Object).Verifiable();

        Repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> predicate, CancellationToken ct) => List(Students.AsQueryable(), predicate, ct))
           .Verifiable();

        Repository
           .Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((Func<IQueryable<Student>, IQueryable<Student>> predicate, CancellationToken _) => predicate(Students.AsQueryable()).SingleOrDefault())
           .Verifiable();

        Repository
           .Setup(r => r.LongCountAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((Func<IQueryable<Student>, IQueryable<Student>> predicate, CancellationToken _) => predicate(Students.AsQueryable()).Count())
           .Verifiable();

        Repository.Setup(r => r.AddAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
                  .Returns((Student entity, CancellationToken _) => {
                       Students.Add(entity);

                       return Task.CompletedTask;
                   })
                  .Verifiable();

        Repository.Setup(r => r.UpdateAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
                  .Returns((Student entity, CancellationToken _) => {
                       var student = Students.First(s => s.Id == entity.Id);
                       var index   = Students.IndexOf(student);

                       Students.RemoveAt(index);
                       Students.Insert(index, entity);

                       return Task.CompletedTask;
                   })
                  .Verifiable();

        Repository.Setup(r => r.RemoveAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
                  .Returns((Student entity, CancellationToken _) => {
                       Students.Remove(entity);

                       return Task.CompletedTask;
                   })
                  .Verifiable();

        Repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken _) => ValueTask.FromResult(0))
                  .Verifiable();

        var url = new Mock<IUrlHelper>(MockBehavior.Strict);
        url.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns(() => "https://example.com").Verifiable();

        var builder = WebApplication.CreateBuilder()
                                    .UseSchemata(schema => {
                                         schema.UseMapster()
                                               .Map<Student, Student>();

                                         schema.UseResource()
                                               .MapHttp();

                                         schema.Services.AddTransient<IRepository<Student>>(_ => Repository.Object);
                                         schema.Services.AddTransient<IUrlHelper>(_ => url.Object);
                                         schema.Services.AddScoped(typeof(ResourceController<,,,>));
                                     });

        var app = builder.Build();

        var scope = app.Services.CreateScope();

        ServiceProvider = scope.ServiceProvider;
    }

    public IServiceProvider ServiceProvider { get; }

    public Mock<IRepository<Student>> Repository { get; } = new(MockBehavior.Strict);

    public List<Student> Students { get; }

    public (ResourceController<TEntity, TRequest, TDetail, TSummary>, MemoryStream) CreateResourceController<
        TEntity, TRequest, TDetail, TSummary>() where TEntity : class, IIdentifier
                                                where TRequest : class, IIdentifier
                                                where TDetail : class, IIdentifier
                                                where TSummary : class, IIdentifier {
        var controller = ServiceProvider.GetRequiredService<ResourceController<TEntity, TRequest, TDetail, TSummary>>();
        var url        = ServiceProvider.GetRequiredService<IUrlHelper>();

        var body = new MemoryStream();
        var context = new DefaultHttpContext {
            RequestServices = ServiceProvider,
            Response        = { Body = body },
        };

        controller.ControllerContext = new() {
            HttpContext = context,
        };
        controller.Url = url;

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
