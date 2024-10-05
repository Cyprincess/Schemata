using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advices;

namespace Schemata.Resource.Http.Tests;

public class TestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; }

    public Mock<IRepository<Student>> Repository { get; } = new();

    public HttpContext Context { get; }

    public MemoryStream Body { get; } = new();

    public ResourceController<Student, Student, Student, Student> Controller { get; }

    public TestFixture() {
        var services = new ServiceCollection();

        services.AddLogging();

        services.Configure<JsonSerializerOptions>(options => {
            options.DictionaryKeyPolicy  = JsonNamingPolicy.SnakeCaseLower;
            options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        });

        services.AddResourceJsonSerializerOptions();

        services.AddScoped(typeof(IResourceResponseAdvice<,>), typeof(AdviceResponseFreshness<,>));

        services.AddControllers();

        var provider = services.BuildServiceProvider();
        var scope    = provider.CreateScope();

        ServiceProvider = scope.ServiceProvider;

        Repository.Setup(r => r.Once()).Returns(() => Repository.Object);
        Repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(() => Repository.Object);

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Student, Student>(It.IsAny<Student>())).Returns<Student>(x => x);

        var serializer = ServiceProvider.GetRequiredService<ResourceJsonSerializerOptions>();

        Context = new DefaultHttpContext {
            RequestServices = ServiceProvider,
            Response        = { Body = Body },
        };

        Controller = new(ServiceProvider, Repository.Object, mapper.Object, serializer) {
            ControllerContext = { HttpContext = Context },
        };
    }

    public void Dispose() {
        Body.Dispose();
    }
}
