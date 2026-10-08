using Schemata.Push.Tests.Fixtures;

namespace Schemata.Push.Tests;

public sealed class EfCorePushSubscriptionRecreateShould : PushSubscriptionRecreateShould
{
    protected override IPushSubscriptionFixture CreateFixture() { return new EfCorePushSubscriptionFixture(); }
}
