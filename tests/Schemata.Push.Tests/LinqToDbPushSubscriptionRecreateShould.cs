using Schemata.Push.Tests.Fixtures;

namespace Schemata.Push.Tests;

public sealed class LinqToDbPushSubscriptionRecreateShould : PushSubscriptionRecreateShould
{
    protected override IPushSubscriptionFixture CreateFixture(bool nameAdvisor, bool blockCreate, bool blockUndelete) {
        return new LinqToDbPushSubscriptionFixture(nameAdvisor, blockCreate, blockUndelete);
    }
}
