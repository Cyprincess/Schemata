namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     The output of signing a message: one labeled entry for the Signature-Input field
///     (RFC 9421 section 4.1) and one for the Signature field (section 4.2).
/// </summary>
/// <param name="Label">The signature label, unique within a message.</param>
/// <param name="Input">The Signature-Input member value: the Inner List of covered components with the signature parameter block.</param>
/// <param name="Value">The Signature member value: the signature bytes as a Byte Sequence (<c>:base64:</c>).</param>
public sealed record HttpMessageSignature(string Label, string Input, string Value)
{
    /// <summary>The Signature-Input field contribution, for example <c>sig=("@method");created=1</c>.</summary>
    public string InputMember => $"{Label}={Input}";

    /// <summary>The Signature field contribution, for example <c>sig=:YWJj:</c>.</summary>
    public string ValueMember => $"{Label}={Value}";
}
