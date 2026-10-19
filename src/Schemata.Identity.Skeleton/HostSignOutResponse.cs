namespace Schemata.Identity.Skeleton;

/// <summary>
///     The response a sign-out observer asks the host to render after its scheme tickets are
///     cleared.
/// </summary>
/// <param name="Body">The response body.</param>
/// <param name="ContentType">The response content type.</param>
public sealed record HostSignOutResponse(string Body, string ContentType);