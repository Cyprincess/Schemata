using Schemata.Push.Tests.Fixtures;

namespace Schemata.Push.Tests;

public sealed class LinqToDbPushSubscriptionRecreateShould : PushSubscriptionRecreateShould
{
    protected override IPushSubscriptionFixture CreateFixture() { return new LinqToDbPushSubscriptionFixture(); }
}
