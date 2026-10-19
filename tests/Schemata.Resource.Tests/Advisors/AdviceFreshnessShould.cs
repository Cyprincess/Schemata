using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceFreshnessShould
{
    [Fact]
    public async Task UpdateFreshness_NoEntityTimestamp_Continues() {
        var advisor = new AdviceUpdateFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.Empty };
        var request = new Student { EntityTag = "W/\"anything\"" };

        var result = await advisor.AdviseAsync(ctx, request, entity, null, new HandledResponse<UpdateResultBase<Student>>());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UpdateFreshness_MismatchedETag_ThrowsAborted() {
        var advisor = new AdviceUpdateFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = "W/\"intentionally-wrong\"" };

        var ex = await Assert.ThrowsAsync<AbortedException>(
            () => advisor.AdviseAsync(ctx, request, entity, null, new HandledResponse<UpdateResultBase<Student>>()));
        AssertEtagMismatch(ex);
    }

    [Fact]
    public async Task UpdateFreshness_MatchingETag_Continues() {
        var advisor   = new AdviceUpdateFreshness<Student, Student, Student>();
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var timestamp = Guid.NewGuid();
        var etag = $"W/\"{
            Convert.ToBase64String(timestamp.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }\"";
        var entity  = new Student { Timestamp = timestamp };
        var request = new Student { EntityTag = etag };

        var result = await advisor.AdviseAsync(ctx, request, entity, null, new HandledResponse<UpdateResultBase<Student>>());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UpdateFreshness_StrongFormatETag_ThrowsAborted() {
        var advisor = new AdviceUpdateFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = "\"strong-tag\"" };

        var ex = await Assert.ThrowsAsync<AbortedException>(
            () => advisor.AdviseAsync(ctx, request, entity, null, new HandledResponse<UpdateResultBase<Student>>()));
        AssertEtagMismatch(ex);
    }

    [Fact]
    public async Task UpdateFreshness_AbsentETag_Continues() {
        var advisor = new AdviceUpdateFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = null };

        var result = await advisor.AdviseAsync(ctx, request, entity, null, new HandledResponse<UpdateResultBase<Student>>());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task DeleteFreshness_StrongFormatETag_ThrowsAborted() {
        var advisor = new AdviceDeleteFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };

        var ex = await Assert.ThrowsAsync<AbortedException>(
            () => advisor.AdviseAsync(ctx, new() { Etag = "\"strong-tag\"" }, entity, null, new HandledResponse<DeleteResultBase<Student>>()));
        AssertEtagMismatch(ex);
    }

    private static void AssertEtagMismatch(AbortedException exception) {
        Assert.NotNull(exception.Details);
        var error = Assert.Single(exception.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal(SchemataConstants.ErrorReasons.ConcurrencyMismatch, error.Reason);
    }

    [Fact]
    public async Task DeleteFreshness_AbsentETag_Continues() {
        var advisor = new AdviceDeleteFreshness<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };

        var result = await advisor.AdviseAsync(ctx, new() { Etag = null }, entity, null, new HandledResponse<DeleteResultBase<Student>>());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task MethodFreshness_StrongFormatETag_ThrowsConcurrencyException() {
        var advisor = new AdviceMethodFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = "\"strong-tag\"" };

        await Assert.ThrowsAsync<AbortedException>(() => advisor.AdviseAsync(
            ctx,
            new ResourceMethodExecution<Student, Student, Student> { Verb = "archive", Request = request, Entity = entity }));
    }

    [Fact]
    public async Task MethodFreshness_MismatchedETag_Carries_The_Resource_Facts() {
        var advisor = new AdviceMethodFreshness<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new Student { CanonicalName = "students/concurrent", Timestamp = Guid.NewGuid() };
        var request = new Student { EntityTag = "\"stale\"" };

        var exception = await Assert.ThrowsAsync<AbortedException>(() => advisor.AdviseAsync(
            ctx,
            new ResourceMethodExecution<Student, Student, Student> { Verb = "archive", Request = request, Entity = entity }));

        var resource = Assert.Single(exception.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal("students/concurrent", resource.ResourceName);
    }
}
