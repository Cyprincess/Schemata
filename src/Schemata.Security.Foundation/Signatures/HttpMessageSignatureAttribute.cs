using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Declares the covered components of an HTTP message signature for a surface: derived
///     components per RFC 9421 section 2.2 (for example <c>@method</c>, <c>@target-uri</c>,
///     <c>@path</c>, <c>@query</c>) and header fields per section 2.1 (for example
///     <c>content-digest</c> when the body participates).
/// </summary>
/// <remarks>
///     Each entry is a component identifier in the textual form of section 2.5, for example
///     <c>"@method"</c>, <c>"content-digest"</c>, or <c>"@query-param";name="Pet"</c>. The same
///     declaration signs and verifies on the HTTP and gRPC transports.
/// </remarks>
/// <example>
///     <code>[HttpMessageSignature("@method", "@authority", "@path", "content-digest")]</code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class HttpMessageSignatureAttribute : Attribute
{
    /// <summary>Creates the marker with the covered component identifiers of the surface.</summary>
    /// <param name="components">The covered component identifiers; at least one is required.</param>
    public HttpMessageSignatureAttribute(params string[] components) {
        Components = components;
    }

    /// <summary>The declared covered component identifiers in textual form.</summary>
    public string[] Components { get; }

    /// <summary>The signature label to use on this surface; <see langword="null" /> for the signer default.</summary>
    public string? Label { get; set; }

    /// <summary>The signature algorithm from the HTTP Signature Algorithms registry (RFC 9421 section 6.2); <see langword="null" /> to infer from the key material.</summary>
    public string? Algorithm { get; set; }

    /// <summary>The key identifier emitted as the <c>keyid</c> signature parameter; <see langword="null" /> when the verifier resolves keys out of band.</summary>
    public string? KeyId { get; set; }

    /// <summary>The application-specific <c>tag</c> signature parameter.</summary>
    public string? Tag { get; set; }

    /// <summary>Parses the declared components into ordered component identifiers.</summary>
    /// <returns>The parsed identifiers in declaration order.</returns>
    /// <exception cref="InvalidArgumentException">The declaration is empty or an entry is malformed.</exception>
    public IReadOnlyList<MessageComponentIdentifier> GetComponents() {
        if (Components.Length == 0) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = string.Empty });
        }

        return Components.Select(MessageComponentIdentifier.Parse).ToArray();
    }
}
