using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The incoming request lacks a resolvable tenant.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.FAILED_PRECONDITION</c> (HTTP 400), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="SchemataResources.TENANT_RESOLUTION_FAILED" /> on
///     <see cref="ErrorInfoDetail" /> plus a <see cref="PreconditionFailureDetail" /> with
///     a <c>TENANT</c> violation entry.
/// </remarks>
public class TenantResolveException : SchemataException
{
    /// <inheritdoc />
    public override string? Domain => ErrorDomains.Tenancy;


    /// <summary>
    ///     Initializes a tenant-resolution failure from a resx key and optional named arguments.
    /// </summary>
    /// <param name="resourceKey">The message template and <see cref="ErrorInfoDetail.Reason" />.</param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public TenantResolveException(
        string resourceKey = SchemataResources.TENANT_RESOLUTION_FAILED,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(400, ErrorCodes.FailedPrecondition, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [
            new ErrorInfoDetail { Reason = resourceKey },
            new PreconditionFailureDetail {
                Violations = [new() {
                    Type        = Keys.Tenancy,
                    Subject     = PreconditionSubjects.Request,
                    Description = LocalizedMessageFormatter.FormatInvariant(resourceKey, args),
                }],
            },
        ];
        AttachMetadata(args);
    }
}
