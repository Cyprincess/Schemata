using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Caching.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceIdempotencyShould
{
    [Fact]
    public async Task Create_NoRequestId_Continues() {
        var cache     = new Mock<ICacheProvider>();
        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new StudentRequest();
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        cache.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NewRequestId_StoresPendingKeyAndContinues() {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        cache.Setup(s => s.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                       It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new StudentRequest { RequestId = "req-123" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        cache.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(
            s => s.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                               It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_ConcurrentRequestId_ThrowsConcurrencyException() {
        // The atomic reservation loses to a concurrent request before a completed result exists.
        var cache = new Mock<ICacheProvider>();
        cache.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        cache.Setup(s => s.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                       It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<SchemataResourceOptions>>(
            Options.Create(new SchemataResourceOptions { IdempotencyPendingWait = TimeSpan.Zero }));

        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx       = new AdviceContext(services.BuildServiceProvider());
        var request   = new StudentRequest { RequestId = "req-concurrent" };
        var container = new ResourceRequestContainer<Student>();

        await Assert.ThrowsAsync<AbortedException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }

    [Fact]
    public async Task Create_SuppressIdempotency_Continues() {
        var cache   = new Mock<ICacheProvider>();
        var advisor = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateIdempotencySuppressed());
        var request   = new StudentRequest { RequestId = "req-456" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        cache.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
