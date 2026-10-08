using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Creates the signature base of RFC 9421 section 2.5: one line per covered component in
///     order, followed by the <c>@signature-params</c> line. Every error the section lists fails
///     creation immediately without output.
/// </summary>
public static class SignatureBase
{
    /// <summary>Builds the signature base string for a message.</summary>
    /// <param name="message">The target message view.</param>
    /// <param name="components">The ordered covered component identifiers; each identifier (including parameters) may appear at most once, and <c>@signature-params</c> must not appear.</param>
    /// <param name="parametersValue">The serialized signature parameters value: the Inner List of covered components plus parameter block, shared verbatim with the Signature-Input field (RFC 9421 section 4.1).</param>
    /// <returns>The ASCII signature base; lines end with a single newline except the final <c>@signature-params</c> line.</returns>
    /// <exception cref="InvalidArgumentException">
    ///     A component identifier is duplicated, unknown, unresolvable from the message, carries
    ///     an unsupported or incompatible parameter, or the resulting base contains non-ASCII
    ///     characters.
    /// </exception>
    public static string Create(
        SignatureMessage                          message,
        IReadOnlyList<MessageComponentIdentifier> components,
        string                                    parametersValue
    ) {
        var builder = new StringBuilder();
        var seen    = new HashSet<string>(StringComparer.Ordinal);

        foreach (var component in components) {
            var identifier = component.ToString();
            if (!seen.Add(identifier)) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = identifier });
            }
            if (component.Name == SignatureConstants.Components.SignatureParams) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = identifier });
            }

            builder.Append(identifier).Append(": ").Append(ResolveValue(message, component)).Append('\n');
        }

        builder.Append('"').Append(SignatureConstants.Components.SignatureParams).Append("\": ").Append(parametersValue);

        var signatureBase = builder.ToString();
        foreach (var character in signatureBase) {
            if (character > 0x7F) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_BASE_NOT_ASCII);
            }
        }

        return signatureBase;
    }

    /// <summary>The UTF-8-free ASCII bytes of a signature base, ready for the HTTP_SIGN primitive.</summary>
    /// <param name="signatureBase">The signature base string.</param>
    /// <returns>The ASCII encoding of <paramref name="signatureBase" />.</returns>
    public static byte[] GetBytes(string signatureBase) {
        return Encoding.ASCII.GetBytes(signatureBase);
    }

    private static string ResolveValue(SignatureMessage message, MessageComponentIdentifier component) {
        var context = message;
        if (component.IsFromRequest) {
            if (!message.IsResponse || message.Request is null) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INCOMPATIBLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
            }

            context = message.Request;
        }

        return component.IsDerived ? ResolveDerived(message, context, component) : ResolveField(context, component);
    }

    private static string ResolveDerived(
        SignatureMessage            target,
        SignatureMessage            context,
        MessageComponentIdentifier  component
    ) {
        if (component.IsBinaryWrapped || component.IsTrailer) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INCOMPATIBLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }
        foreach (var (key, value) in component.Parameters) {
            if (key is SignatureConstants.ComponentParameters.Sf or SignatureConstants.ComponentParameters.Key) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNSUPPORTED, new Dictionary<string, string?> { ["component"] = component.Name, ["parameter"] = key });
            }

            // Section 2.4: req draws the component from the related request, which has no status.
            var applicable = key switch {
                SignatureConstants.ComponentParameters.Req  => value is null && component.Name != SignatureConstants.Components.Status,
                SignatureConstants.ComponentParameters.Name => value is not null && component.Name == SignatureConstants.Components.QueryParam,
                _                                           => false,
            };
            if (!applicable) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INCOMPATIBLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
            }
        }

        return component.Name switch {
            SignatureConstants.Components.Method       => Required(context.Method, component),
            SignatureConstants.Components.TargetUri    => TargetUri(context, component),
            SignatureConstants.Components.Authority    => Required(context.Authority, component),
            SignatureConstants.Components.Scheme       => Required(context.Scheme, component),
            SignatureConstants.Components.RequestTarget => RequestTarget(context, component),
            SignatureConstants.Components.Path         => string.IsNullOrEmpty(context.Path) ? "/" : context.Path,
            SignatureConstants.Components.Query        => context.Query ?? "?",
            SignatureConstants.Components.QueryParam   => QueryParam(context, component),
            SignatureConstants.Components.Status       => Required(target.Status?.ToString(), component),
            _ => throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() }),
        };
    }

    private static string Required(string? value, MessageComponentIdentifier component) {
        if (value is null) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }

        return value;
    }

    private static string TargetUri(SignatureMessage context, MessageComponentIdentifier component) {
        if (context.Scheme is null || context.Authority is null) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }

        return $"{context.Scheme}://{context.Authority}{RequestTarget(context, component)}";
    }

    private static string RequestTarget(SignatureMessage context, MessageComponentIdentifier component) {
        if (context.Path is null) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }

        var path = string.IsNullOrEmpty(context.Path) ? "/" : context.Path;
        return context.Query is null ? path : path + context.Query;
    }

    private static string QueryParam(SignatureMessage context, MessageComponentIdentifier component) {
        var name = component.ParameterName;
        if (string.IsNullOrEmpty(name) || context.Query is null) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }

        string? value  = null;
        var     found  = false;
        foreach (var pair in context.Query[1..].Split('&')) {
            var equal    = pair.IndexOf('=');
            var rawName  = equal < 0 ? pair : pair[..equal];
            if (!string.Equals(FormUrlEncoded.Decode(rawName), name, StringComparison.Ordinal)) {
                continue;
            }

            if (found) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
            }

            found = true;
            value = FormUrlEncoded.Decode(equal < 0 ? string.Empty : pair[(equal + 1)..]);
        }

        if (!found) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }

        return FormUrlEncoded.Encode(value!);
    }

    private static string ResolveField(SignatureMessage context, MessageComponentIdentifier component) {
        foreach (var (key, value) in component.Parameters) {
            if (key is SignatureConstants.ComponentParameters.Sf or SignatureConstants.ComponentParameters.Key) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNSUPPORTED, new Dictionary<string, string?> { ["component"] = component.Name, ["parameter"] = key });
            }
            if (key is not (SignatureConstants.ComponentParameters.Bs or SignatureConstants.ComponentParameters.Tr or SignatureConstants.ComponentParameters.Req) || value is not null) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INCOMPATIBLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
            }
        }


        var values = context.GetFieldValues(component.Name, component.IsTrailer);
        if (values.Count == 0) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_UNRESOLVABLE, new Dictionary<string, string?> { ["component"] = component.ToString() });
        }

        if (component.IsBinaryWrapped) {
            var wrapped = new string[values.Count];
            for (var index = 0; index < values.Count; index++) {
                wrapped[index] = ":" + Convert.ToBase64String(Encoding.ASCII.GetBytes(Canonicalize(values[index]))) + ":";
            }

            return string.Join(", ", wrapped);
        }

        return string.Join(", ", values.Select(Canonicalize));
    }

    // RFC 9421 section 2.1: strip leading and trailing whitespace and replace obsolete line
    // folding with a single space before combining instances with ", ".
    private static string Canonicalize(string value) {
        var unfolded = value.Replace("\r\n\t", " ", StringComparison.Ordinal).Replace("\r\n ", " ", StringComparison.Ordinal);
        return unfolded.Trim(' ', '\t');
    }
}
