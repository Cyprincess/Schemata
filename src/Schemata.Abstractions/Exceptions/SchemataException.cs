using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Base exception for all Schemata error conditions.
/// </summary>
/// <remarks>
///     Carries an HTTP status code, a canonical <c>google.rpc.Code</c>, and optional typed
///     detail entries per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Middleware produces structured error responses from this information
///     through the common exception base type.
/// </remarks>
public class SchemataException : Exception
{
    /// <summary>
    ///     Initializes a new <see cref="SchemataException" />.
    /// </summary>
    /// <param name="code">
    ///     HTTP response status code returned to the API consumer.
    /// </param>
    /// <param name="status">
    ///     Canonical error code from <c>google.rpc.Code</c> for client-side branching
    ///     (e.g. <c>"NOT_FOUND"</c>).
    /// </param>
    /// <param name="message">
    ///     Developer-oriented diagnostic message for logs and API clients.
    /// </param>
    public SchemataException(int code, string? status = null, string? message = null) : base(message) {
        Code   = code;
        Status = status;
    }

    /// <summary>
    ///     HTTP response status code returned to the API consumer.
    /// </summary>
    public int Code { get; }

    /// <summary>
    ///     Canonical error code for client-side branching; drawn from <c>google.rpc.Code</c>
    ///     enum values.
    /// </summary>
    public string? Status { get; }

    /// <summary>
    ///     Typed detail entries providing additional structured information about the error.
    /// </summary>
    public List<IErrorDetail>? Details { get; set; }

    /// <summary>
    ///     Copies <paramref name="args" /> onto the first <see cref="ErrorInfoDetail" /> in
    ///     <see cref="Details" /> so the resx template can rehydrate its named placeholders
    ///     during locale resolution. Convenience constructors on subclasses call this after
    ///     chaining to the base ctor that creates the <see cref="ErrorInfoDetail" />.
    /// </summary>
    /// <param name="args">Named arguments for the resx template; <see langword="null" /> or
    ///     empty leaves the existing detail untouched.</param>
    protected void AttachMetadata(IReadOnlyDictionary<string, string?>? args) {
        if (args is not { Count: > 0 }) {
            return;
        }

        var info = Details?.OfType<ErrorInfoDetail>().FirstOrDefault();
        if (info is null) {
            return;
        }

        info.Metadata = LocalizedMessageFormatter.NormalizeMetadata(args);
    }

    /// <summary>
    ///     Builds the error response envelope returned by the API.
    /// </summary>
    /// <remarks>
    ///     Subclasses may override to produce protocol-specific envelopes — for example,
    ///     <see cref="OAuthException" /> returns an <see cref="OAuthErrorResponse" /> per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
    ///     for protocol-specific error serialization.
    /// </remarks>
    /// <param name="requestId">Optional request identifier included in <see cref="RequestInfoDetail" />.</param>
    /// <param name="domain">Optional ErrorInfo domain.</param>
    /// <param name="locale">
    ///     Optional <seealso href="https://www.rfc-editor.org/rfc/bcp/bcp47.html">BCP-47</seealso>
    ///     language tag parsed from the transport's <c>Accept-Language</c> header. When
    ///     supplied and resolvable, a <see cref="LocalizedMessageDetail" /> is appended via
    ///     <see cref="EnsureLocalizedMessage" />.
    /// </param>
    public virtual object? CreateErrorResponse(string? requestId = null, string? domain = null, string? locale = null) {
        var status  = Status ?? ErrorCodes.Internal;
        var details = new List<IErrorDetail>();

        if (Details is { Count: > 0 }) {
            details.AddRange(Details);
        }

        EnsureErrorInfo(details, status, domain);
        EnsureRequestInfo(details, requestId);
        EnsureLocalizedMessage(details, locale, status);

        return new ErrorResponse {
            Error = new() {
                Code    = Code,
                Message = Message,
                Status  = status,
                Details = details,
            },
        };
    }

    /// <summary>
    ///     Adds an <see cref="ErrorInfoDetail" /> when the detail list lacks one.
    /// </summary>
    /// <param name="details">Mutable detail list for the response.</param>
    /// <param name="reason">Canonical reason code assigned to the inserted detail.</param>
    /// <param name="domain">Logical service domain assigned to the inserted detail.</param>
    protected static void EnsureErrorInfo(List<IErrorDetail> details, string reason, string? domain) {
        if (details.Any(d => d is ErrorInfoDetail)) {
            return;
        }

        details.Insert(0, new ErrorInfoDetail { Reason = reason, Domain = domain, });
    }

    /// <summary>
    ///     Adds a <see cref="RequestInfoDetail" /> when a request identifier is available.
    /// </summary>
    /// <param name="details">Mutable detail list for the response.</param>
    /// <param name="requestId">Request identifier to include in the detail list.</param>
    protected static void EnsureRequestInfo(List<IErrorDetail> details, string? requestId) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        if (details.Any(d => d is RequestInfoDetail)) {
            return;
        }

        details.Add(new RequestInfoDetail { RequestId = requestId });
    }

    /// <summary>
    ///     Appends a <see cref="LocalizedMessageDetail" /> when a locale resolves a resx
    ///     template; otherwise the detail list is left untouched.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         AIP-193 splits the response shape into three independent identifiers:
    ///         <see cref="Status" /> (a <c>google.rpc.Code</c> name such as
    ///         <c>NOT_FOUND</c>) for top-level classification,
    ///         <see cref="ErrorInfoDetail.Reason" /> for a domain-specific further
    ///         identifier (e.g. <c>RESOURCE_NOT_FOUND</c>) on which clients branch, and
    ///         <see cref="LocalizedMessageDetail" /> for the user-facing text.
    ///     </para>
    ///     <para>
    ///         The lookup tries the <see cref="ErrorInfoDetail.Reason" /> resx key first,
    ///         then falls back to the <c>Status</c> resx key. This keeps a localized
    ///         template available even when a specific Reason has no dedicated resx
    ///         entry. When the template carries named placeholders (e.g.
    ///         <c>{resource}</c>) the helper substitutes them from
    ///         <see cref="ErrorInfoDetail.Metadata" /> by key, which makes the wire
    ///         contract independent of dictionary enumeration order. Templates that
    ///         still use positional placeholders (e.g. <c>{0}</c>) fall through to
    ///         <see cref="string.Format(IFormatProvider, string, object?[])" /> with
    ///         <see cref="Dictionary{TKey, TValue}.Values" /> in insertion order. The
    ///         helper silently skips on any failure — unresolvable locale, missing
    ///         resx keys, or template format error — so localization never interferes
    ///         with the developer-facing <see cref="Exception.Message" />.
    ///     </para>
    /// </remarks>
    /// <param name="details">Mutable detail list for the response.</param>
    /// <param name="locale">BCP-47 language tag parsed from <c>Accept-Language</c>.</param>
    /// <param name="status">Top-level <c>google.rpc.Code</c> name used as the resx fallback key when Reason has no dedicated entry.</param>
    protected static void EnsureLocalizedMessage(List<IErrorDetail> details, string? locale, string? status) {
        if (string.IsNullOrWhiteSpace(locale)) {
            return;
        }

        if (details.Any(d => d is LocalizedMessageDetail)) {
            return;
        }

        var errorInfo = details.OfType<ErrorInfoDetail>().FirstOrDefault();
        var reason    = errorInfo?.Reason;

        CultureInfo culture;
        try {
            culture = CultureInfo.GetCultureInfo(locale);
        } catch (CultureNotFoundException) {
            return;
        }

        var template = TryGetResource(reason, culture) ?? TryGetResource(status, culture);
        var message  = LocalizedMessageFormatter.Format(template, errorInfo?.Metadata, culture);
        if (string.IsNullOrEmpty(message)) {
            return;
        }

        details.Add(new LocalizedMessageDetail {
            Locale  = locale,
            Message = message,
        });
    }

    private static string? TryGetResource(string? key, CultureInfo culture) {
        if (key is not { Length: > 0 }) {
            return null;
        }

        try {
            return SchemataResources.ResourceManager.GetString(key, culture);
        } catch (Exception) {
            return null;
        }
    }
}
