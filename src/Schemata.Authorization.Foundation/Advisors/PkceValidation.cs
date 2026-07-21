namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Shared RFC 7636 shape validation for <c>code_verifier</c> and <c>code_challenge</c>, used by
///     <see cref="AdviceAuthorizePkce{TApp}" /> and <see cref="AdviceCodeExchangePkce{TApp, TToken}" />.
/// </summary>
internal static class PkceValidation
{
    /// <summary>
    ///     Returns <see langword="true" /> when <paramref name="value" /> is 43–128 characters long and
    ///     consists solely of unreserved characters (ALPHA / DIGIT / "-" / "." / "_" / "~"), per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.1">
    ///         RFC 7636: Proof Key for Code Exchange by OAuth Public Clients §4.1: Client Creates the Code Verifier
    ///     </seealso>
    ///     .
    /// </summary>
    /// <param name="value">The verifier or challenge to validate; assumed non-null and non-empty.</param>
    public static bool IsValid(string value) {
        if (value.Length is < 43 or > 128) {
            return false;
        }

        foreach (var c in value) {
            if (!IsUnreserved(c)) {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnreserved(char c) {
        return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~';
    }
}
