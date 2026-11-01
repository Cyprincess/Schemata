using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Signals a successful operation whose response contains no body.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.OK</c>, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     The error-response pipeline suppresses JSON serialization and returns HTTP 204
///     with an empty body.
/// </remarks>
public class NoContentException : SchemataException
{
    /// <summary>Initializes the fixed HTTP 204 / <c>OK</c> control-flow result.</summary>
    public NoContentException() : base(204, ErrorCodes.Ok) { }

    public override object? CreateErrorResponse(string? requestId = null, string? locale = null) {
        return null;
    }
}
