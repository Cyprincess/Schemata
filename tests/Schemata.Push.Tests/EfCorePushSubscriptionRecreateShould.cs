using Schemata.Push.Tests.Fixtures;

namespace Schemata.Push.Tests;

public sealed class EfCorePushSubscriptionRecreateShould : PushSubscriptionRecreateShould
{
    protected override IPushSubscriptionFixture CreateFixture(bool nameAdvisor, bool blockCreate, bool blockUndelete) {
        return new EfCorePushSubscriptionFixture(nameAdvisor, blockCreate, blockUndelete);
    }
}
