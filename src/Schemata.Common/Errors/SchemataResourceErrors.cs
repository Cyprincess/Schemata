using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common.Errors;

/// <summary>
///     Factory for resource-themed Schemata exceptions. Every method produces an exception
///     pre-populated with a <see cref="ResourceInfoDetail" /> identifying the resource type
///     and canonical name, plus an <see cref="ErrorInfoDetail" /> carrying a domain-specific
///     AIP-193 reason (defaulting to the matching <see cref="ErrorReasons" /> entry, which
///     is intentionally more specific than the top-level <c>google.rpc.Code</c>). The
///     transport layer's central <c>EnsureLocalizedMessage</c> attaches the localized
///     message from the resx bundle, falling back from Reason to Status when the reason
///     has no dedicated entry.
/// </summary>
/// <remarks>
///     <para>
///         The exception's developer-facing <c>Message</c> is the English invariant from the
///         resx bundle; resource type and canonical name are <em>not</em> templated into the
///         message because they are already carried structurally by
///         <see cref="ResourceInfoDetail" />.
///     </para>
///     <para>
///         Per <seealso href="https://google.aip.dev/211">AIP-211</seealso>, the
///         <see cref="ResourceInfoDetail.Owner" /> field is left unset on <c>NOT_FOUND</c>
///         paths to avoid leaking existence; it is permitted on <c>PERMISSION_DENIED</c>
///         where existence is already acknowledged.
///     </para>
///     <para>
///         Every factory accepts an optional <c>reason</c> parameter. Throw sites with
///         finer context should override the default (for example
///         <c>NotFound&lt;SchemataUser&gt;(name, reason: "USER_NOT_FOUND")</c>) so clients
///         branch on the specific failure mode the throw site identifies.
///     </para>
/// </remarks>
public static class SchemataResourceErrors
{
    /// <summary>
    ///     Builds a <see cref="NotFoundException" /> for a named resource of type
    ///     <typeparamref name="T" />. The <see cref="ResourceInfoDetail.Owner" /> field is
    ///     intentionally omitted (AIP-211).
    /// </summary>
    /// <typeparam name="T">The resource entity type.</typeparam>
    /// <param name="name">The canonical resource name, or <see langword="null" /> when unavailable.</param>
    /// <param name="description">Optional human-readable context.</param>
    /// <param name="reason">
    ///     Domain-specific reason. Defaults to <see cref="ErrorReasons.ResourceNotFound" />;
    ///     supply a more specific value when one is known.
    /// </param>
    /// <returns>The constructed exception, ready to <c>throw</c>.</returns>
    public static NotFoundException NotFound<T>(
        string? name        = null,
        string? description = null,
        string  reason      = ErrorReasons.ResourceNotFound) {
        return NotFound(typeof(T), name, description, reason);
    }

    /// <summary>
    ///     Non-generic overload used when the resource type is only known at runtime.
    /// </summary>
    /// <param name="type">The resource entity type.</param>
    /// <param name="name">The canonical resource name, or <see langword="null" /> when unavailable.</param>
    /// <param name="description">Optional human-readable context.</param>
    /// <param name="reason">Domain-specific reason; defaults to <see cref="ErrorReasons.ResourceNotFound" />.</param>
    public static NotFoundException NotFound(
        System.Type type,
        string?     name        = null,
        string?     description = null,
        string      reason      = ErrorReasons.ResourceNotFound) {
        var descriptor = ResourceNameDescriptor.ForType(type);
        var exception  = new NotFoundException(reason: null);
        exception.Details = [
            new ErrorInfoDetail { Reason = reason },
            new ResourceInfoDetail {
                ResourceType = descriptor.Singular,
                ResourceName = name,
                Description  = description,
            },
        ];
        return exception;
    }

    /// <summary>
    ///     Builds an <see cref="AlreadyExistsException" /> for a named resource of type
    ///     <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The resource entity type.</typeparam>
    /// <param name="name">The canonical resource name of the conflicting resource.</param>
    /// <param name="description">Optional human-readable context.</param>
    /// <param name="reason">Domain-specific reason; defaults to <see cref="ErrorReasons.ResourceAlreadyExists" />.</param>
    public static AlreadyExistsException AlreadyExists<T>(
        string? name        = null,
        string? description = null,
        string  reason      = ErrorReasons.ResourceAlreadyExists) {
        var descriptor = ResourceNameDescriptor.ForType<T>();
        var exception  = new AlreadyExistsException(reason: null);
        exception.Details = [
            new ErrorInfoDetail { Reason = reason },
            new ResourceInfoDetail {
                ResourceType = descriptor.Singular,
                ResourceName = name,
                Description  = description,
            },
        ];
        return exception;
    }

    /// <summary>
    ///     Builds a <see cref="FailedPreconditionException" /> for a named resource of type
    ///     <typeparamref name="T" />. Attaches a single
    ///     <see cref="PreconditionViolation" /> whose <see cref="PreconditionViolation.Subject" />
    ///     identifies the specific precondition (e.g.
    ///     <c>SOFT_DELETED</c>, <c>ETAG_MISMATCH</c>).
    /// </summary>
    /// <typeparam name="T">The resource entity type.</typeparam>
    /// <param name="name">The canonical resource name.</param>
    /// <param name="subject">The precondition subject identifier.</param>
    /// <param name="description">Optional human-readable context.</param>
    /// <param name="reason">Domain-specific reason; defaults to <see cref="ErrorReasons.PreconditionNotSatisfied" />.</param>
    public static FailedPreconditionException PreconditionFailed<T>(
        string? name        = null,
        string? subject     = null,
        string? description = null,
        string  reason      = ErrorReasons.PreconditionNotSatisfied) {
        var descriptor = ResourceNameDescriptor.ForType<T>();
        var exception  = new FailedPreconditionException(reason: null);
        exception.Details = [
            new ErrorInfoDetail { Reason = reason },
            new ResourceInfoDetail {
                ResourceType = descriptor.Singular,
                ResourceName = name,
                Description  = description,
            },
            new PreconditionFailureDetail {
                Violations = [
                    new() {
                        Type        = descriptor.Singular,
                        Subject     = subject,
                        Description = description,
                    },
                ],
            },
        ];
        return exception;
    }

    /// <summary>
    ///     Builds a <see cref="PermissionDeniedException" /> for a named resource of type
    ///     <typeparamref name="T" />. The <paramref name="owner" /> field is propagated to
    ///     <see cref="ResourceInfoDetail.Owner" />; existence has already been acknowledged
    ///     so this does not leak under AIP-211.
    /// </summary>
    /// <typeparam name="T">The resource entity type.</typeparam>
    /// <param name="name">The canonical resource name.</param>
    /// <param name="owner">Optional canonical name of the resource owner.</param>
    /// <param name="description">Optional human-readable context.</param>
    /// <param name="reason">Domain-specific reason; defaults to <see cref="ErrorReasons.InsufficientPermission" />.</param>
    public static PermissionDeniedException PermissionDenied<T>(
        string? name        = null,
        string? owner       = null,
        string? description = null,
        string  reason      = ErrorReasons.InsufficientPermission) {
        var descriptor = ResourceNameDescriptor.ForType<T>();
        var exception  = new PermissionDeniedException(reason: null);
        exception.Details = [
            new ErrorInfoDetail { Reason = reason },
            new ResourceInfoDetail {
                ResourceType = descriptor.Singular,
                ResourceName = name,
                Owner        = owner,
                Description  = description,
            },
        ];
        return exception;
    }

    /// <summary>
    ///     Builds an <see cref="AbortedException" /> for a named resource of type
    ///     <typeparamref name="T" />. Use this when the abort is resource-themed; for
    ///     generic optimistic-concurrency errors prefer the
    ///     <see cref="AbortedException" /> constructor directly, which already attaches
    ///     <see cref="ErrorReasons.ConcurrencyMismatch" />.
    /// </summary>
    /// <typeparam name="T">The resource entity type.</typeparam>
    /// <param name="name">The canonical resource name.</param>
    /// <param name="description">Optional human-readable context.</param>
    /// <param name="reason">Domain-specific reason; defaults to <see cref="ErrorReasons.ConcurrencyMismatch" />.</param>
    public static AbortedException Aborted<T>(
        string? name        = null,
        string? description = null,
        string  reason      = ErrorReasons.ConcurrencyMismatch) {
        var descriptor = ResourceNameDescriptor.ForType<T>();
        var exception  = new AbortedException(reason: null);
        exception.Details = [
            new ErrorInfoDetail { Reason = reason },
            new ResourceInfoDetail {
                ResourceType = descriptor.Singular,
                ResourceName = name,
                Description  = description,
            },
        ];
        return exception;
    }
}
