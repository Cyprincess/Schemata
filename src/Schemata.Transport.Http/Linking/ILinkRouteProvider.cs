using Microsoft.AspNetCore.Http;

namespace Schemata.Transport.Http.Linking;

public interface ILinkRouteProvider
{
    string? GetRoute(string operation);
    string? GetOperation(HttpRequest request);
}
