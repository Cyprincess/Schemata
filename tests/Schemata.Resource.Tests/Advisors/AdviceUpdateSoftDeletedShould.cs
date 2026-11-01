using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceUpdateSoftDeletedShould
{
    [Fact]
    public async Task SoftDeletedEntity_ThrowsFailedPrecondition() {
        var advisor = new AdviceUpdateSoftDeleted<TrashStudent, TrashStudent, TrashStudent>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new TrashStudent { CanonicalName = "trashStudents/alice-1", DeleteTime = DateTime.UtcNow };

        await Assert.ThrowsAsync<FailedPreconditionException>(() => advisor.AdviseAsync(ctx, new(), entity, null, new HandledResponse<UpdateResultBase<TrashStudent>>()));
    }

    [Fact]
    public async Task LiveEntity_Continues() {
        var advisor = new AdviceUpdateSoftDeleted<TrashStudent, TrashStudent, TrashStudent>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var entity  = new TrashStudent { CanonicalName = "trashStudents/alice-1", DeleteTime = null };

        var result = await advisor.AdviseAsync(ctx, new(), entity, null, new HandledResponse<UpdateResultBase<TrashStudent>>());

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task NonSoftDeleteEntity_Continues() {
        var advisor = new AdviceUpdateSoftDeleted<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        var result = await advisor.AdviseAsync(ctx, new(), new(), null, new HandledResponse<UpdateResultBase<Student>>());

        Assert.Equal(AdviseResult.Continue, result);
    }
}
