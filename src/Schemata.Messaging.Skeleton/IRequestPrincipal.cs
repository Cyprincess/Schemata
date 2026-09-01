using System.Security.Claims;

namespace Schemata.Messaging.Skeleton;

/// <summary>Exposes the authenticated caller attached by a transport before request dispatch.</summary>
public interface IRequestPrincipal
{
    ClaimsPrincipal? Principal { get; set; }
}
