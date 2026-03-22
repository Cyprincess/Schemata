using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Advisors;

public class StudentRequest : Student, IRequestIdentification
{
    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}

public class AdviceIdempotencyShould
{
    [Fact]
    public async Task Create_NoRequestId_Continues() {
        var store   = new Mock<IIdempotencyStore>();
        var advisor = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(store.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new StudentRequest();

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
        store.Verify(s => s.GetAsync<CreateResult<Student>>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NewRequestId_StoresPendingKeyAndContinues() {
        var store = new Mock<IIdempotencyStore>();
        store.Setup(s => s.GetAsync<CreateResult<Student>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((CreateResult<Student>?)null);

        var advisor = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(store.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new StudentRequest { RequestId = "req-123" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
        store.Verify(s => s.GetAsync<CreateResult<Student>>("req-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateRequestId_ReturnsCachedResultAndHandles() {
        var cached = new CreateResult<Student> { Detail = new() { FullName = "Cached" } };
        var store  = new Mock<IIdempotencyStore>();
        store.Setup(s => s.GetAsync<CreateResult<Student>>("req-123", It.IsAny<CancellationToken>()))
             .ReturnsAsync(cached);

        var advisor = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(store.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new StudentRequest { RequestId = "req-123" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(cached, ctx.Get<CreateResult<Student>>());
    }

    [Fact]
    public async Task Create_SuppressIdempotency_Continues() {
        var store   = new Mock<IIdempotencyStore>();
        var advisor = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(store.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressCreateIdempotency());
        var request = new StudentRequest { RequestId = "req-456" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
        store.Verify(s => s.GetAsync<CreateResult<Student>>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
