using System.Security.Claims;
using Schemata.Messaging.Skeleton;

namespace Schemata.Security.Tests.Fixtures;

public sealed class TestRequest(ClaimsPrincipal? principal) : IRequest<string>, IRequestPrincipal
{
    public ClaimsPrincipal? Principal { get; set; } = principal;
}