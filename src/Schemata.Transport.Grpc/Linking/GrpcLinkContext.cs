using Grpc.Core;
using Schemata.Core.Building;

namespace Schemata.Transport.Grpc.Linking;

/// <summary>
///     The call and response state available to an <see cref="IGrpcLinkContributor" />.
/// </summary>
/// <param name="Call">The current server call.</param>
/// <param name="Response">The response being shaped, or <see langword="null" />.</param>
/// <param name="Resource">The resource registration the response belongs to, or <see langword="null" />.</param>
public sealed record GrpcLinkContext(
    ServerCallContext Call,
    object? Response,
    ResourceRegistration? Resource);
