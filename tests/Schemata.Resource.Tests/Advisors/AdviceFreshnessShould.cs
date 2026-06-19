using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceFreshnessShould
{
    [Fact]
    public async Task UpdateFreshness_NoEntityTimestamp_Continues() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.Empty };
        var request = new Student { EntityTag = "W/\"anything\"" };

        var result = await advisor.AdviseAsync(ctx, request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UpdateFreshness_MismatchedETag_ThrowsConcurrencyException() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };
        var request = new Student { EntityTag = "W/\"intentionally-wrong\"" };

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(ctx, request, entity, null));
    }

    [Fact]
    public async Task UpdateFreshness_MatchingETag_Continues() {
        var advisor   = new AdviceUpdateFreshness<Student, Student>();
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var timestamp = Identifiers.NewUid();
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
        ctx.Set(new FreshnessSuppressed());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };
        var request = new Student { EntityTag = "W/\"wrong\"" };

        var result = await advisor.AdviseAsync(ctx, request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UpdateFreshness_StrongFormatETag_ThrowsConcurrencyException() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };
        var request = new Student { EntityTag = "\"strong-tag\"" };

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(ctx, request, entity, null));
    }

    [Fact]
    public async Task UpdateFreshness_AbsentETag_Continues() {
        var advisor = new AdviceUpdateFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };
        var request = new Student { EntityTag = null };

        var result = await advisor.AdviseAsync(ctx, request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task DeleteFreshness_StrongFormatETag_ThrowsConcurrencyException() {
        var advisor = new AdviceDeleteFreshness<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(
                                                           ctx, new() { Etag = "\"strong-tag\"" }, entity, null));
    }

    [Fact]
    public async Task DeleteFreshness_AbsentETag_Continues() {
        var advisor = new AdviceDeleteFreshness<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };

        var result = await advisor.AdviseAsync(ctx, new() { Etag = null }, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task MethodFreshness_StrongFormatETag_ThrowsConcurrencyException() {
        var advisor = new AdviceMethodFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Identifiers.NewUid() };
        var request = new Student { EntityTag = "\"strong-tag\"" };

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(ctx, request, entity, null));
    }

    [Fact]
    public async Task ResponseFreshness_EmptyTimestamp_ProducesNoETag() {
        var advisor = new AdviceResponseFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.Empty };
        var detail  = new Student();

        var result = await advisor.AdviseAsync(ctx, entity, detail, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(detail.EntityTag);
    }

    [Fact]
    public async Task ResponseFreshness_Timestamp_ProducesWeakETag() {
        var advisor   = new AdviceResponseFreshness<Student, Student>();
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var timestamp = Identifiers.NewUid();
        var expected = $"W/\"{
            Convert.ToBase64String(timestamp.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }\"";
        var entity = new Student { Timestamp = timestamp };
        var detail = new Student();

        var result = await advisor.AdviseAsync(ctx, entity, detail, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(expected, detail.EntityTag);
    }
}
