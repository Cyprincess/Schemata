using System;
using System.Collections.Generic;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Schemata.Security.Foundation.Signatures;

namespace Schemata.Transport.Grpc.Extensions;

/// <summary>
///     The gRPC binding for message signatures: adapts gRPC metadata and call context into the
///     shared <see cref="SignatureMessage" /> view of the security signing core and applies
///     signature output back onto metadata. gRPC runs over HTTP/2, so the same RFC 9421 surface
///     definition (method <c>POST</c>, path <c>/package.Service/Method</c>, authority, selected
///     metadata) signs and verifies without per-transport signature logic.
/// </summary>
public static class GrpcSignatureMessageExtensions
{
    /// <summary>Adapts gRPC metadata into the shared message view.</summary>
    /// <param name="metadata">The request metadata; binary (<c>-bin</c>) entries are excluded because covered field values must be ASCII.</param>
    /// <param name="method">The full gRPC method, for example <c>/package.Service/Method</c>; becomes the <c>@path</c> of the view.</param>
    /// <param name="host">The target authority; becomes the <c>@authority</c> of the view, normalized per RFC 9421 section 2.2.3.</param>
    /// <param name="scheme">The URI scheme of the call; defaults to <c>https</c>.</param>
    /// <returns>The message view; the HTTP method of a gRPC call is <c>POST</c>.</returns>
    public static SignatureMessage ToSignatureMessage(
        this Metadata metadata,
        string        method,
        string?       host   = null,
        string        scheme = "https"
    ) {
        var fields = new List<SignatureField>();
        foreach (var entry in metadata) {
            if (!entry.IsBinary) {
                fields.Add(new(entry.Key, entry.Value));
            }
        }

        return new(
            method: HttpMethods.Post,
            scheme: scheme.ToLowerInvariant(),
            authority: Authority(scheme, host),
            path: method,
            fields: fields
        );
    }

    /// <summary>Adapts a server-side call context into the shared message view, using the context's method, host, and request metadata.</summary>
    /// <param name="context">The server call context.</param>
    /// <param name="scheme">The URI scheme of the call; defaults to <c>https</c>.</param>
    /// <returns>The message view.</returns>
    public static SignatureMessage ToSignatureMessage(this ServerCallContext context, string scheme = "https") {
        return context.RequestHeaders.ToSignatureMessage(context.Method, context.Host, scheme);
    }

    /// <summary>Adds the signature output to metadata as <c>signature-input</c> and <c>signature</c> entries.</summary>
    /// <param name="metadata">The metadata receiving the entries.</param>
    /// <param name="signature">The signature output.</param>
    public static void ApplySignature(this Metadata metadata, HttpMessageSignature signature) {
        metadata.Add(SignatureConstants.Fields.SignatureInput, signature.InputMember);
        metadata.Add(SignatureConstants.Fields.Signature, signature.ValueMember);
    }

    // RFC 9421 section 2.2.3: lowercase host, default port omitted. An IPv6 literal is only
    // split at a bracketed "]:" port; an unbracketed literal (invalid per RFC 9110 section
    // 4.2.4 but possible in metadata) is treated as a host without a port.
    private static string? Authority(string scheme, string? host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return host;
        }

        var separator = host.StartsWith('[')
            ? host.IndexOf("]:", StringComparison.Ordinal) is var bracket && bracket > 0 ? bracket + 1 : -1
            : host.IndexOf(':') == host.LastIndexOf(':') ? host.LastIndexOf(':') : -1;
        if (separator < 0 || !int.TryParse(host[(separator + 1)..], out var port)) {
            return host.ToLowerInvariant();
        }

        var isDefault = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) && port == 443
                     || string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) && port == 80;
        return isDefault ? host[..separator].ToLowerInvariant() : host.ToLowerInvariant();
    }
}
