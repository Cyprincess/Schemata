using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Signals a successful operation whose response contains no body.
/// </summary>
/// <remarks>
///     This is <em>not</em> a failure — it maps to <c>google.rpc.Code.OK</c>, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     The error-response pipeline must suppress JSON serialization and return HTTP 204
///     with an empty body.
/// </remarks>
public class NoContentException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="NoContentException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public NoContentException(
        int     code    = 204,
        string? status  = ErrorCodes.Ok,
        string? message = null
    ) : base(code, status, message) { }

    public override object? CreateErrorResponse(string? requestId = null, string? domain = null) { return null; }
}
