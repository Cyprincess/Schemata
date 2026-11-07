using Schemata.Abstractions.Exceptions;

namespace Schemata.Quota.Foundation.Handlers;

public static class QuotaControlRequestGuard
{
    public static void Validate(string? scope, string? tenant) {
        if (scope is not null) throw new InvalidArgumentException();
        if (tenant is not null) throw new InvalidArgumentException();
    }
}
