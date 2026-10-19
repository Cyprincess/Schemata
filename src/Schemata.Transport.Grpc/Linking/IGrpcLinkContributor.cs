using System.Collections.Generic;
using Schemata.Abstractions.Linking;

namespace Schemata.Transport.Grpc.Linking;

/// <summary>
///     Contributes links for the current gRPC response. The shared interceptor collects every
///     registered contributor and writes the links once as the <c>link</c> response trailer.
/// </summary>
public interface IGrpcLinkContributor
{
    /// <summary>
    ///     Returns the links for the supplied response state.
    /// </summary>
    /// <param name="context">The call and response being shaped.</param>
    /// <returns>The contributed links.</returns>
    IEnumerable<WebLink> GetLinks(GrpcLinkContext context);
}
