using System.Collections.Generic;
using System.Linq;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>A single HTTP field instance: name plus value, one entry per occurrence in the message.</summary>
/// <param name="Name">The field name; stored lowercased per RFC 9421 section 2.1.</param>
/// <param name="Value">The field value as sent, before signature canonicalization.</param>
public sealed record SignatureField(string Name, string Value)
{
    /// <summary>The field name lowercased per RFC 9421 section 2.1.</summary>
    public string Name { get; } = Name.ToLowerInvariant();
}

/// <summary>
///     A transport-neutral view of an HTTP message for RFC 9421 signing and verification. HTTP
///     requests, responses, and gRPC calls (HTTP/2 message layer) all adapt into this single
///     shape, so one covered-component definition signs and verifies on every transport.
/// </summary>
/// <remarks>
///     A view with <see cref="Status" /> set is a response target; <see cref="Request" /> then
///     carries the related request that <c>req</c>-flagged components (RFC 9421 section 2.4)
///     draw their values from.
/// </remarks>
public sealed class SignatureMessage
{
    /// <summary>Creates a message view. All control-data members are optional; resolving a covered component whose input is absent fails signature base creation.</summary>
    /// <param name="method">The HTTP method (requests only).</param>
    /// <param name="scheme">The lowercase URI scheme (requests only).</param>
    /// <param name="authority">The normalized authority: lowercase host, default port omitted (requests only).</param>
    /// <param name="path">The absolute path in escaped form, without query.</param>
    /// <param name="query">The query including the leading <c>?</c>, or <see langword="null" /> when absent.</param>
    /// <param name="status">The three-digit status code (responses only).</param>
    /// <param name="fields">The header field instances of the message.</param>
    /// <param name="trailers">The trailer field instances of the message.</param>
    /// <param name="request">The related request of a response target.</param>
    public SignatureMessage(
        string?                       method    = null,
        string?                       scheme    = null,
        string?                       authority = null,
        string?                       path      = null,
        string?                       query     = null,
        int?                          status    = null,
        IEnumerable<SignatureField>?  fields    = null,
        IEnumerable<SignatureField>?  trailers  = null,
        SignatureMessage?             request   = null
    ) {
        Method    = method;
        Scheme    = scheme;
        Authority = authority;
        Path      = path;
        Query     = query;
        Status    = status;
        Fields    = fields?.ToArray() ?? [];
        Trailers  = trailers?.ToArray() ?? [];
        Request   = request;
    }

    /// <summary>The HTTP method, or <see langword="null" /> for a response target.</summary>
    public string? Method { get; }

    /// <summary>The lowercase URI scheme.</summary>
    public string? Scheme { get; }

    /// <summary>The normalized authority (lowercase host, default port omitted).</summary>
    public string? Authority { get; }

    /// <summary>The absolute path in escaped form, without query.</summary>
    public string? Path { get; }

    /// <summary>The query including the leading <c>?</c>, or <see langword="null" /> when absent.</summary>
    public string? Query { get; }

    /// <summary>The status code, or <see langword="null" /> for a request target.</summary>
    public int? Status { get; }

    /// <summary>Whether the target is a response message.</summary>
    public bool IsResponse => Status.HasValue;

    /// <summary>The header field instances in message order.</summary>
    public IReadOnlyList<SignatureField> Fields { get; }

    /// <summary>The trailer field instances in message order.</summary>
    public IReadOnlyList<SignatureField> Trailers { get; }

    /// <summary>The related request of a response target, or <see langword="null" />.</summary>
    public SignatureMessage? Request { get; }

    /// <summary>The values of every instance of the named field, in message order.</summary>
    /// <param name="name">The field name; compared lowercased per RFC 9421 section 2.1.</param>
    /// <param name="trailer"><see langword="true" /> to read trailer fields instead of header fields.</param>
    /// <returns>The ordered instance values; empty when the field is absent.</returns>
    public IReadOnlyList<string> GetFieldValues(string name, bool trailer = false) {
        var source = trailer ? Trailers : Fields;
        var lower  = name.ToLowerInvariant();
        return source.Where(field => field.Name == lower).Select(field => field.Value).ToArray();
    }
}
