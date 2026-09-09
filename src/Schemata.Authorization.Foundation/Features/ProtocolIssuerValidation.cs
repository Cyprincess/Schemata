using System;
using Schemata.Authorization.Foundation.Authentication;

namespace Schemata.Authorization.Foundation.Features;

internal static class ProtocolIssuerValidation
{
    public static void RequireHttps(SchemataAuthorizationOptions options, string protocol) {
        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer)
            || issuer.Scheme != Uri.UriSchemeHttps) {
            throw new InvalidOperationException($"{protocol} requires an HTTPS issuer.");
        }
    }
}
