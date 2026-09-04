using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Registry-backed validation of the raw <c>authorization_details</c> JSON array per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-5">
///         RFC 9396: OAuth 2.0 Rich Authorization Requests §5: Authorization Error Response
///     </seealso>
///     .
///     Parameter-structure violations — non-JSON values, non-array roots, non-object elements,
///     and objects lacking a string <c>type</c> — fail as <see cref="OAuthErrors.InvalidRequest" />:
///     §5 scopes <c>invalid_authorization_details</c> to conformance conditions evaluated "of the
///     objects" against a type definition, and an object without a usable <c>type</c> cannot be
///     dispatched to one, leaving a malformed parameter under RFC 6749's <c>invalid_request</c>.
///     Unknown <c>type</c> values (§5 bullet 1) and descriptor rejections (§5 bullets 2-5:
///     unknown fields, wrong-typed fields, invalid values, or missing required fields) fail as
///     <see cref="OAuthErrors.InvalidAuthorizationDetails" />.
/// </summary>
public sealed class AuthorizationDetailsService(IEnumerable<IAuthorizationDetailTypeDescriptor> descriptors)
{
    private readonly Dictionary<string, IAuthorizationDetailTypeDescriptor> _descriptors = descriptors.ToDictionary(
        d => d.Type);

    /// <summary>
    ///     Parses and validates the raw parameter per RFC 9396 §5: a JSON array whose
    ///     elements are objects carrying a <c>type</c> member; every type must be registered.
    ///     Returns the normalized array, or throws <see cref="OAuthException" />
    ///     (<c>invalid_authorization_details</c> / <c>invalid_request</c>) otherwise.
    /// </summary>
    /// <param name="raw">Raw form-decoded parameter value, or <c>null</c> when absent.</param>
    /// <param name="ct">Cancellation token for authorization pipeline integration; unused by the synchronous parse.</param>
    /// <returns>
    ///     An independently owned array mirroring the requested details; empty when the
    ///     parameter is absent.
    /// </returns>
    public JsonArray Parse(string? raw, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return [];
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_AUTHORIZATION_DETAILS_JSON));
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_AUTHORIZATION_DETAILS_ARRAY));
        }

        var details = new JsonArray();
        foreach (var element in root.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.Object) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_AUTHORIZATION_DETAILS_OBJECT));
            }

            if (!element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_AUTHORIZATION_DETAILS_TYPE_MEMBER));
            }

            var name = type.GetString()!;
            if (!_descriptors.TryGetValue(name, out var descriptor)) {
                throw new OAuthException(
                    OAuthErrors.InvalidAuthorizationDetails,
                    string.Format(
                        SchemataResources.GetResourceString(
                            SchemataResources.INVALID_AUTHORIZATION_DETAILS_TYPE_UNSUPPORTED),
                        name));
            }

            var message = descriptor.Validate(element.Clone());
            if (message is not null) {
                throw new OAuthException(OAuthErrors.InvalidAuthorizationDetails, message);
            }

            details.Add(JsonNode.Parse(element.GetRawText()));
        }

        return details;
    }
}

