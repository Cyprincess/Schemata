using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Advisors;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceOperationHandler;

public class MethodOperationHandlerShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Invoke_CollectionScope_PassesNullEntity() {
        var handler   = new RecordingHandler();
        var operation = CreateOperation();

        var response = await operation.InvokeAsync(handler, "run", null, new(), null, null);

        Assert.NotNull(response);
        Assert.True(handler.Invoked);
        Assert.Null(handler.Entity);
    }

    [Fact]
    public async Task Invoke_CollectionScope_EmptyCollection_Invokes() {
        _fixture.Students.Clear();
        var handler   = new RecordingHandler();
        var operation = CreateOperation();

        var response = await operation.InvokeAsync(handler, "run", null, new(), null, null);

        Assert.NotNull(response);
        Assert.True(handler.Invoked);
    }

    [Fact]
    public async Task Invoke_CollectionScope_SkipsEntityAdvisors() {
        var advisor = new RecordingAdvisor();
        var handler = new RecordingHandler();
        var operation = CreateOperation(services => {
            services.AddSingleton<IResourceMethodAdvisor<Student, EmptyResourceRequest, Student>>(advisor);
        });

        await operation.InvokeAsync(handler, "run", null, new(), null, null);

        Assert.False(advisor.Invoked);
        Assert.True(handler.Invoked);
    }

    [Fact]
    public async Task Invoke_InstanceScope_LoadsEntity() {
        var handler   = new RecordingHandler();
        var operation = CreateOperation();

        await operation.InvokeAsync(handler, "run", "students/alice-1", new(), null, null);

        Assert.True(handler.Invoked);
        Assert.Equal("alice-1", handler.Entity?.Name);
    }

    [Fact]
    public async Task Invoke_InstanceScope_RunsEntityAdvisors() {
        var advisor = new RecordingAdvisor();
        var handler = new RecordingHandler();
        var operation = CreateOperation(services => {
            services.AddSingleton<IResourceMethodAdvisor<Student, EmptyResourceRequest, Student>>(advisor);
        });

        await operation.InvokeAsync(handler, "run", "students/alice-1", new(), null, null);

        Assert.True(advisor.Invoked);
    }

    [Fact]
    public async Task Invoke_InstanceScope_WithoutFreshness_SuppressesMethodFreshness() {
        var handler  = new RecordingStudentHandler();
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SchemataResourceOptions { SuppressFreshness = true }));
        services.AddSingleton<IResourceMethodAdvisor<Student, Student, Student>>(
            new AdviceMethodFreshness<Student, Student, Student>());
        var operation = new ResourceMethodOperationHandler<Student, Student, Student>(
            _fixture.Repository.Object,
            services.BuildServiceProvider());

        var response = await operation.InvokeAsync(
            handler,
            "run",
            "students/alice-1",
            new() { EntityTag = "W/\"stale\"" },
            null,
            null);

        Assert.Equal("result", response.Name);
    }

    [Fact]
    public async Task Invoke_InstanceScope_Missing_ThrowsNotFound() {
        var handler   = new RecordingHandler();
        var operation = CreateOperation();

        await Assert.ThrowsAsync<NotFoundException>(
            () => operation.InvokeAsync(handler, "run", "students/zoe-9", new(), null, null));
    }

    private ResourceMethodOperationHandler<Student, EmptyResourceRequest, Student> CreateOperation(
        Action<IServiceCollection>? configure = null
    ) {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return new(_fixture.Repository.Object, sp);
    }

    private sealed class RecordingHandler : IResourceMethodHandler<Student, EmptyResourceRequest, Student>
    {
        public Student? Entity;
        public bool     Invoked;

        public ValueTask<Student> InvokeAsync(
            string?              name,
            EmptyResourceRequest request,
            Student?             entity,
            ClaimsPrincipal?     principal,
            CancellationToken    ct
        ) {
            Invoked = true;
            Entity  = entity;
            return ValueTask.FromResult(new Student { Name = "result", CanonicalName = "students/result" });
        }
    }

    private sealed class ThrowingHandler : IResourceMethodHandler<Student, EmptyResourceRequest, Student>
    {
        public ValueTask<Student> InvokeAsync(
            string?              name,
            EmptyResourceRequest request,
            Student?             entity,
            ClaimsPrincipal?     principal,
            CancellationToken    ct
        ) {
            throw new InvalidOperationException("handler failure");
        }
    }

    private sealed class ThrowingStudentHandler : IResourceMethodHandler<Student, StudentRequest, Student>
    {
        public ValueTask<Student> InvokeAsync(
            string?          name,
            StudentRequest   request,
            Student?         entity,
            ClaimsPrincipal? principal,
            CancellationToken ct
        ) {
            throw new InvalidOperationException("handler failure");
        }
    }

    private sealed class RecordingStudentHandler : IResourceMethodHandler<Student, Student, Student>
    {
        public ValueTask<Student> InvokeAsync(
            string?          name,
            Student          request,
            Student?         entity,
            ClaimsPrincipal? principal,
            CancellationToken ct
        ) {
            return ValueTask.FromResult(new Student { Name = "result", CanonicalName = "students/result" });
        }
    }

    private sealed class RecordingAdvisor : IResourceMethodAdvisor<Student, EmptyResourceRequest, Student>
    {
        public bool Invoked;

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext        ctx,
            EmptyResourceRequest request,
            Student              entity,
            ClaimsPrincipal?     principal,
            CancellationToken    ct = default
        ) {
            Invoked = true;
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
