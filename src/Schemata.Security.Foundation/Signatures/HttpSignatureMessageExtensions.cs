using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Http;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     The HTTP binding for message signatures: adapts <see cref="HttpRequestMessage" />,
///     <see cref="HttpResponseMessage" />, <see cref="HttpRequest" />, and
///     <see cref="HttpResponse" /> into the shared <see cref="SignatureMessage" /> view and
///     applies signature output back onto them. Signing and verification logic stays in
///     <see cref="HttpMessageSigner" /> and <see cref="HttpMessageSignatureVerifier" />.
/// </summary>
public static class HttpSignatureMessageExtensions
{
    /// <summary>Adapts an outgoing or received HTTP request into the shared message view.</summary>
    /// <param name="message">The request message.</param>
    /// <returns>The message view; content headers are included as fields.</returns>
    public static SignatureMessage ToSignatureMessage(this HttpRequestMessage message) {
        var fields = new List<SignatureField>();
        foreach (var (name, values) in message.Headers) {
            foreach (var value in values) {
                fields.Add(new(name, value));
            }
        }

        if (message.Content is { } content) {
            foreach (var (name, values) in content.Headers) {
                foreach (var value in values) {
                    fields.Add(new(name, value));
                }
            }
        }

        var uri = message.RequestUri;
        return new(
            method: message.Method.Method,
            scheme: uri?.Scheme.ToLowerInvariant(),
            authority: uri is { IsAbsoluteUri: true } ? uri.Authority : message.Headers.Host,
            path: uri is { IsAbsoluteUri: true } ? uri.AbsolutePath : null,
            query: uri is { IsAbsoluteUri: true } && !string.IsNullOrEmpty(uri.Query) ? uri.Query : null,
            fields: fields
        );
    }

    /// <summary>Adapts an HTTP response into the shared message view.</summary>
    /// <param name="message">The response message.</param>
    /// <param name="request">The view of the request that triggered this response; required when covered components carry the <c>req</c> flag (RFC 9421 section 2.4).</param>
    /// <returns>The message view; content headers are included as fields.</returns>
    public static SignatureMessage ToSignatureMessage(this HttpResponseMessage message, SignatureMessage? request = null) {
        var fields = new List<SignatureField>();
        foreach (var (name, values) in message.Headers) {
            foreach (var value in values) {
                fields.Add(new(name, value));
            }
        }

        if (message.Content is { } content) {
            foreach (var (name, values) in content.Headers) {
                foreach (var value in values) {
                    fields.Add(new(name, value));
                }
            }
        }

        return new(status: (int)message.StatusCode, fields: fields, request: request);
    }

    /// <summary>Adapts a server-side HTTP request into the shared message view.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The message view.</returns>
    public static SignatureMessage ToSignatureMessage(this HttpRequest request) {
        var fields = new List<SignatureField>();
        foreach (var (name, values) in request.Headers) {
            foreach (var value in values) {
                if (value is not null) {
                    fields.Add(new(name, value));
                }
            }
        }

        return new(
            method: request.Method,
            scheme: request.Scheme.ToLowerInvariant(),
            authority: Authority(request.Scheme, request.Host.Host, request.Host.Port),
            path: request.Path.Value,
            query: request.QueryString.HasValue ? request.QueryString.Value : null,
            fields: fields
        );
    }

    /// <summary>Adapts a server-side HTTP response into the shared message view.</summary>
    /// <param name="response">The response.</param>
    /// <param name="request">The view of the request that triggered this response; required when covered components carry the <c>req</c> flag (RFC 9421 section 2.4).</param>
    /// <returns>The message view.</returns>
    public static SignatureMessage ToSignatureMessage(this HttpResponse response, SignatureMessage? request = null) {
        var fields = new List<SignatureField>();
        foreach (var (name, values) in response.Headers) {
            foreach (var value in values) {
                if (value is not null) {
                    fields.Add(new(name, value));
                }
            }
        }

        return new(status: response.StatusCode, fields: fields, request: request);
    }

    /// <summary>Adds the signature output to a request message as Signature-Input and Signature field values.</summary>
    /// <param name="message">The request message.</param>
    /// <param name="signature">The signature output.</param>
    public static void ApplySignature(this HttpRequestMessage message, HttpMessageSignature signature) {
        message.Headers.TryAddWithoutValidation(SignatureConstants.Fields.SignatureInput, signature.InputMember);
        message.Headers.TryAddWithoutValidation(SignatureConstants.Fields.Signature, signature.ValueMember);
    }

    /// <summary>Adds the signature output to a server-side message as Signature-Input and Signature field values.</summary>
    /// <param name="response">The response receiving the fields.</param>
    /// <param name="signature">The signature output.</param>
    public static void ApplySignature(this HttpResponse response, HttpMessageSignature signature) {
        response.Headers.Append(SignatureConstants.Fields.SignatureInput, signature.InputMember);
        response.Headers.Append(SignatureConstants.Fields.Signature, signature.ValueMember);
    }

    // RFC 9421 section 2.2.3: lowercase host, default port omitted.
    private static string Authority(string scheme, string host, int? port) {
        var lower = host.ToLowerInvariant();
        var isDefault = port is null
                     || (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) && port == 443)
                     || (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) && port == 80);
        return isDefault ? lower : $"{lower}:{port}";
    }
}
