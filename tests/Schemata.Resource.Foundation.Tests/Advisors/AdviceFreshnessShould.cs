using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Advisors;

public class AdviceFreshnessShould
{
    [Fact]
    public async Task UpdateFreshness_NoEntityTimestamp_Continues() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = null };
        var request = new Student { EntityTag = "W/\"anything\"" };

        var result = await advisor.AdviseAsync(ctx, request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UpdateFreshness_MismatchedETag_ThrowsConcurrencyException() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = "W/\"intentionally-wrong\"" };

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(ctx, request, entity, null));
    }

    [Fact]
    public async Task UpdateFreshness_MatchingETag_Continues() {
        var advisor   = new AdviceUpdateFreshness<Student, Student>();
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var timestamp = Guid.NewGuid();
        var etag = $"W/\"{
            Convert.ToBase64String(timestamp.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }\"";
        var entity  = new Student { Timestamp = timestamp };
        var request = new Student { EntityTag = etag };

        var result = await advisor.AdviseAsync(ctx, request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UpdateFreshness_SuppressFreshness_Continues() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressFreshness());
        var entity  = new Student { Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = "W/\"wrong\"" };

        var result = await advisor.AdviseAsync(ctx, request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }
}
