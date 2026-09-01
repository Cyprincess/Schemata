using System;

namespace Schemata.Identity.Foundation.Handlers;

internal static class IdentityRequestHandler
{
    internal static T Require<T>(T? request) where T : class {
        ArgumentNullException.ThrowIfNull(request);
        return request;
    }
}