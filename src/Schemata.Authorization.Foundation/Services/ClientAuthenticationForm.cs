using System.Collections.Generic;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Rebuilds the complete client-authentication form input from a bound request model. Grant and
///     endpoint handlers receive the model, not the raw form, so every rebuild must go through here:
///     dropping a field (client_assertion, most notoriously) silently removes an entire
///     authentication method from the wire.
/// </summary>
internal static class ClientAuthenticationForm
{
    public static Dictionary<string, List<string?>> Build(
        string? clientId,
        string? clientSecret,
        string? clientAssertion     = null,
        string? clientAssertionType = null
    ) {
        // Only absent (null) inputs are omitted: a field the caller actually received — even an
        // empty value — keeps its entry so presented-mechanism detection and valueless-field
        // errors see the real wire shape. Constructing client_id=[null] would fabricate a
        // presented field where none existed.
        var form = new Dictionary<string, List<string?>>();

        if (clientId is not null) {
            form[Parameters.ClientId] = [clientId];
        }

        if (clientSecret is not null) {
            form[Parameters.ClientSecret] = [clientSecret];
        }

        if (clientAssertion is not null) {
            form[Parameters.ClientAssertion] = [clientAssertion];
        }

        if (clientAssertionType is not null) {
            form[Parameters.ClientAssertionType] = [clientAssertionType];
        }

        return form;
    }
}
