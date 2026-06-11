using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceOperationHandler;

public class OperationHandlerBlockShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Get_BlockedByRequestAdvisor_ThrowsNotFound() {
        var handler = CreateBlockingHandler();

        await Assert.ThrowsAsync<NotFoundException>(() => handler.GetAsync("students/alice-1", null, null));
    }

    [Fact]
    public async Task List_BlockedByRequestAdvisor_ThrowsNotFound() {
        var handler = CreateBlockingHandler();

        await Assert.ThrowsAsync<NotFoundException>(() => handler.ListAsync(new(), null, null));
    }

    [Fact]
    public async Task Create_BlockedByRequestAdvisor_ThrowsNotFound() {
        var handler = CreateBlockingHandler();

        await Assert.ThrowsAsync<NotFoundException>(() => handler.CreateAsync(new(), null, null));
    }

    [Fact]
    public async Task Delete_BlockedByRequestAdvisor_ThrowsNotFound() {
        var handler = CreateBlockingHandler();

        await Assert.ThrowsAsync<NotFoundException>(() => handler.DeleteAsync("students/alice-1", null, null, null));
    }

    [Fact]
    public async Task Get_HandledWithResult_ReturnsStashedResult() {
        var handler = _fixture.CreateHandler(services => {
            services.AddSingleton<IResourceRequestAdvisor<Student>>(
                new HandlingAdvisor(new() { Detail = new() { FullName = "Stashed" } }));
        });

        var result = await handler.GetAsync("students/alice-1", null, null);

        Assert.Equal("Stashed", result.Detail?.FullName);
    }

    private ResourceOperationHandler<Student, Student, Student, Student> CreateBlockingHandler() {
        return _fixture.CreateHandler(services => {
            services.AddSingleton<IResourceRequestAdvisor<Student>>(new BlockingAdvisor());
        });
    }

    private sealed class BlockingAdvisor : IResourceRequestAdvisor<Student>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext     ctx,
            ClaimsPrincipal?  principal,
            string            operation,
            CancellationToken ct = default
        ) {
            return Task.FromResult(AdviseResult.Block);
        }
    }

    private sealed class HandlingAdvisor(GetResultBase<Student> result) : IResourceRequestAdvisor<Student>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext     ctx,
            ClaimsPrincipal?  principal,
            string            operation,
            CancellationToken ct = default
        ) {
            ctx.Set(result);
            return Task.FromResult(AdviseResult.Handle);
        }
    }
}
