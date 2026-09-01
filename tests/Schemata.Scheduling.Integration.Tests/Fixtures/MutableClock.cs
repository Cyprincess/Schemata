using System;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public DateTime Now => _now.UtcDateTime;

    public override DateTimeOffset GetUtcNow() { return _now; }
}
