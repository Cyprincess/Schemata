using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class IdempotencyProcess : ProcessDefinition
{
    public IdempotencyProcess() {
        this.Start().Go(Review);
        this.During(Review).End();
    }

    public UserTask Review { get; } = null!;
}