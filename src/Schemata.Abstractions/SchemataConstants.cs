namespace Schemata.Abstractions;

/// <summary>
///     Well-known constants used throughout the Schemata framework.
/// </summary>
public static class SchemataConstants
{
    /// <summary>
    ///     The Schemata framework identifier GUID.
    /// </summary>
    public const string Schemata = "9049a32e-c96b-4e0e-ae34-c370c574f00d";

    #region Nested type: ErrorCodes

    /// <summary>
    ///     Standard machine-readable error codes following the Google API error model.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>No error.</summary>
        public const string Ok = "OK";

        /// <summary>The request contained invalid arguments.</summary>
        public const string InvalidArgument = "INVALID_ARGUMENT";

        /// <summary>The requested resource was not found.</summary>
        public const string NotFound = "NOT_FOUND";

        /// <summary>The caller does not have permission.</summary>
        public const string PermissionDenied = "PERMISSION_DENIED";

        /// <summary>The operation was aborted (e.g., concurrency conflict).</summary>
        public const string Aborted = "ABORTED";

        /// <summary>The resource already exists.</summary>
        public const string AlreadyExists = "ALREADY_EXISTS";

        /// <summary>A precondition for the operation was not met.</summary>
        public const string FailedPrecondition = "FAILED_PRECONDITION";

        /// <summary>The caller is not authenticated.</summary>
        public const string Unauthenticated = "UNAUTHENTICATED";

        /// <summary>A quota or rate limit was exceeded.</summary>
        public const string ResourceExhausted = "RESOURCE_EXHAUSTED";

        /// <summary>An internal server error occurred.</summary>
        public const string Internal = "INTERNAL";
    }

    #endregion

    #region Nested type: ErrorReasons

    /// <summary>
    ///     Machine-readable reason codes for structured error details.
    /// </summary>
    public static class ErrorReasons
    {
        /// <summary>The concurrency token did not match.</summary>
        public const string ConcurrencyMismatch = "CONCURRENCY_MISMATCH";
    }

    #endregion

    #region Nested type: FieldReasons

    /// <summary>
    ///     Machine-readable reason codes for field-level validation violations.
    /// </summary>
    public static class FieldReasons
    {
        /// <summary>The field is required but was not provided.</summary>
        public const string Required = "REQUIRED";

        /// <summary>The request payload is invalid.</summary>
        public const string InvalidPayload = "INVALID_PAYLOAD";

        /// <summary>The filter expression is invalid.</summary>
        public const string InvalidFilter = "INVALID_FILTER";

        /// <summary>The order_by expression is invalid.</summary>
        public const string InvalidOrderBy = "INVALID_ORDER_BY";

        /// <summary>The page token is invalid or expired.</summary>
        public const string InvalidPageToken = "INVALID_PAGE_TOKEN";

        /// <summary>Cross-parent operations are not supported.</summary>
        public const string CrossParentUnsupported = "CROSS_PARENT_UNSUPPORTED";
    }

    #endregion

    #region Nested type: Options

    /// <summary>
    ///     Well-known option keys used in <see cref="Schemata.Abstractions" />.
    /// </summary>
    public static class Options
    {
        /// <summary>Key for the features dictionary in SchemataOptions.</summary>
        public const string Features = "Features";

        /// <summary>Key for the modular modules list in configuration.</summary>
        public const string ModularModules = "Modular:Modules";
    }

    #endregion

    #region Nested type: Orders

    /// <summary>
    ///     Well-known ordering constants.
    /// </summary>
    public static class Orders
    {
        /// <summary>Base anchor for built-in feature and advisor ordering chains.</summary>
        public const int Base = 100_000_000;

        /// <summary>Base anchor for extension feature ordering chains.</summary>
        public const int Extension = Base + 300_000_000;

        /// <summary>Terminal anchor for advisors and features that must run near the end of a pipeline.</summary>
        public const int Max = 900_000_000;
    }

    #endregion

    #region Nested type: Parameters

    /// <summary>
    ///     Well-known parameter names used in serialization and API conventions.
    /// </summary>
    public static class Parameters
    {
        /// <summary>The entity tag parameter name.</summary>
        public const string EntityTag = "etag";

        /// <summary>The type discriminator parameter name for polymorphic serialization.</summary>
        public const string Type = "@type";
    }

    #endregion

    #region Nested type: PreconditionSubjects

    /// <summary>
    ///     Well-known precondition subjects.
    /// </summary>
    public static class PreconditionSubjects
    {
        /// <summary>The request itself is the subject.</summary>
        public const string Request = "request";
    }

    #endregion

    #region Nested type: PreconditionTypes

    /// <summary>
    ///     Well-known precondition type identifiers.
    /// </summary>
    public static class PreconditionTypes
    {
        /// <summary>A tenant precondition.</summary>
        public const string Tenant = "TENANT";
    }

    #endregion
}
