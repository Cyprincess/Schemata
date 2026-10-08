using System.Security.Claims;

namespace Schemata.Flow.Foundation;

internal static class FlowSystemPrincipal
{
    internal static ClaimsPrincipal Instance { get; } = new(new ClaimsIdentity("Schemata.Flow.Infrastructure"));
}
