using System.Collections.Generic;
using Schemata.Abstractions.Errors;
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
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public NoContentException(
        int     status  = 204,
        string? code    = ErrorCodes.Ok,
        string? message = null
    ) : base(status, code, message) { }

    /// <summary>
    ///     Returns <see langword="null" /> to suppress response body serialization.
    /// </summary>
    /// <remarks>
    ///     HTTP 204 responses must carry no body; the pipeline skips serialization
    ///     when a <see langword="null" /> value is returned.
    /// </remarks>
    /// <param name="details">Ignored — no body is produced regardless.</param>
    public override object? CreateErrorResponse(IEnumerable<IErrorDetail>? details = null) { return null; }
}
