namespace Schemata.Authorization.Foundation.Commands;

/// <summary>
///     The accepted <c>authorization_details</c> grant set, published on the ambient advice
///     context by <c>AdviceAuthorizeAuthorizationDetails</c> after RFC 9396 validation and read
///     back by the grant/interaction paths. Absent from the context, the raw parameter stays
///     bound but inert: hosts without the rich-authorization feature neither validate nor grant it.
/// </summary>
/// <param name="Json">The normalized grant set serialized as a JSON array.</param>
public sealed record AuthorizationDetailsGrant(string Json);
