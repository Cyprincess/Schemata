using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Request validation failed with field-level violation details.
/// </summary>
/// <remarks>
///     Carries the canonical status <c>google.rpc.Code.INVALID_ARGUMENT</c>, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>, and defaults to
///     HTTP 422 so that validation failures stay distinguishable from malformed requests.
///     <c>google.rpc.Code</c> maps <c>INVALID_ARGUMENT</c> to HTTP 400, which is the default
///     <see cref="InvalidArgumentException" /> uses.
///     Attaches <see cref="ErrorReasons.ValidationFailed" /> on
///     <see cref="BadRequestDetail.FieldViolations" />.
/// </remarks>
public sealed class ValidationException : SchemataException
{
    /// <inheritdoc />
    public override string? Domain => ErrorDomains.Validation;

    /// <summary>
    ///     Initializes a validation failure carrying field-level violations.
    /// </summary>
    /// <param name="errors">Individual field violations.</param>
    /// <param name="resourceKey">The message template; the reason stays <see cref="ErrorReasons.ValidationFailed" />.</param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public ValidationException(
        IEnumerable<ErrorFieldViolation> errors,
        string resourceKey = SchemataResources.VALIDATION_ERROR,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(422, ErrorCodes.InvalidArgument, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [
            new ErrorInfoDetail { Reason = ErrorReasons.ValidationFailed },
            new BadRequestDetail { FieldViolations = errors.ToList() },
        ];
        AttachMetadata(args);
    }
}
