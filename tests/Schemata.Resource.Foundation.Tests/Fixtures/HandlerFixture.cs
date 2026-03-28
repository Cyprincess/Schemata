using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;

namespace Schemata.Resource.Foundation.Tests.Fixtures;

/// <summary>
///     Shared fixture for ResourceOperationHandler unit tests.
///     Wires a real handler with Moq-backed repository and mapper.
///     No advisors are registered by default — add via Services as needed.
/// </summary>
public class HandlerFixture
{
    public HandlerFixture() {
        // Identity mapper: returns the input object cast to destination type
        Mapper.Setup(m => m.Map<Student>(It.IsAny<object>())).Returns<object>(src => src as Student ?? new Student());
        Mapper.Setup(m => m.Map<Student, Student>(It.IsAny<Student>())).Returns<Student>(src => src);

        // Field-selective mapper for UpdateMask: copies named properties from source to destination
        Mapper.Setup(m => m.Map(It.IsAny<Student>(), It.IsAny<Student>(), It.IsAny<IEnumerable<string>>()))
              .Callback<Student, Student, IEnumerable<string>>((src, dst, fields) => {
                   foreach (var field in fields) {
                       var prop = typeof(Student).GetProperty(field);
                       prop?.SetValue(dst, prop.GetValue(src));
                   }
               });

        // Full-object mapper (no fields): copy all public properties
        Mapper.Setup(m => m.Map(It.IsAny<Student>(), It.IsAny<Student>()))
              .Callback<Student, Student>((src, dst) => {
                   foreach (var prop in typeof(Student).GetProperties()) {
                       if (prop.CanWrite) prop.SetValue(dst, prop.GetValue(src));
                   }
               });

        Repository.Setup(r => r.Once()).Returns(Repository.Object);
        Repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Repository.Object);

        Repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken ct) => AsyncList(q(Students.AsQueryable()), ct));

        Repository
           .Setup(r => r.SingleOrDefaultAsync<Student>(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken _) => ValueTask.FromResult(q(Students.AsQueryable()).SingleOrDefault()));

        Repository
           .Setup(r => r.CountAsync<Student>(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken _) => ValueTask.FromResult(q(Students.AsQueryable()).Count()));

        Repository
           .Setup(r => r.LongCountAsync<Student>(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken _) => ValueTask.FromResult((long)q(Students.AsQueryable()).Count()));

        Repository.Setup(r => r.AddAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
                  .Returns((Student s, CancellationToken _) => {
                       Students.Add(s);
                       return Task.CompletedTask;
                   });

        Repository.Setup(r => r.UpdateAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
                  .Returns((Student s, CancellationToken _) => {
                       var idx                     = Students.FindIndex(x => x.Id == s.Id);
                       if (idx >= 0) Students[idx] = s;
                       return Task.CompletedTask;
                   });

        Repository.Setup(r => r.RemoveAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
                  .Returns((Student s, CancellationToken _) => {
                       Students.Remove(s);
                       return Task.CompletedTask;
                   });

        Repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(ValueTask.FromResult(0));
    }

    public Mock<IRepository<Student>> Repository { get; } = new();
    public Mock<ISimpleMapper>        Mapper     { get; } = new();

    public List<Student> Students { get; } = [
        new() {
            Id            = 1,
            FullName      = "Alice",
            Age           = 18,
            Grade         = 1,
            Name          = "alice-1",
            CanonicalName = "students/alice-1",
            Timestamp     = Guid.NewGuid(),
        },
        new() {
            Id            = 2,
            FullName      = "Bob",
            Age           = 19,
            Grade         = 2,
            Name          = "bob-2",
            CanonicalName = "students/bob-2",
            Timestamp     = Guid.NewGuid(),
        },
    ];

    public ResourceOperationHandler<Student, Student, Student, Student> CreateHandler(
        Action<IServiceCollection>? configure = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SchemataResourceOptions()));
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return new(sp, Repository.Object, Mapper.Object);
    }

    private static async IAsyncEnumerable<T> AsyncList<T>(
        IQueryable<T>                              source,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();
            yield return await Task.FromResult(item);
        }
    }
}
