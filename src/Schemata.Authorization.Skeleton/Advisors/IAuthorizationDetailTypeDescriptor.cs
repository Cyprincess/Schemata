using System.Text.Json;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Validates one authorization details type per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-5">
///         RFC 9396: OAuth 2.0 Rich Authorization Requests §5: Authorization Error Response
///     </seealso>
///     .
///     Hosts register one descriptor per <c>type</c> value the authorization server supports;
///     the registry refuses unknown types with <c>invalid_authorization_details</c> before any
///     detail is accepted for processing.
/// </summary>
public interface IAuthorizationDetailTypeDescriptor
{
    /// <summary>The <c>type</c> value this descriptor accepts.</summary>
    string Type { get; }

    /// <summary>
    ///     Validates the detail object structure; returns an error message on failure.
    /// </summary>
    /// <remarks>
    ///     Covers the type-definition conformance conditions of RFC 9396 §5: unknown fields,
    ///     fields of the wrong type, fields with invalid values, and missing required fields.
    /// </remarks>
    /// <param name="detail">The <c>authorization_details</c> object to validate.</param>
    /// <returns>A human-readable error message when the object does not conform; otherwise <c>null</c>.</returns>
    string? Validate(JsonElement detail);
}
