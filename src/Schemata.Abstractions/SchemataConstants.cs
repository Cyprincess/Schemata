namespace Schemata.Abstractions;

/// <summary>
///     Well-known constant values used across the Schemata framework.
/// </summary>
public static class SchemataConstants
{
    /// <summary>
    ///     Schemata framework identifier as a GUID string.
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

        /// <summary>The requested resource is missing.</summary>
        public const string NotFound = "NOT_FOUND";

        /// <summary>The caller lacks permission.</summary>
        public const string PermissionDenied = "PERMISSION_DENIED";

        /// <summary>The operation aborts because of a conflict.</summary>
        public const string Aborted = "ABORTED";

        /// <summary>The resource already exists.</summary>
        public const string AlreadyExists = "ALREADY_EXISTS";

        /// <summary>A precondition for the operation failed.</summary>
        public const string FailedPrecondition = "FAILED_PRECONDITION";

        /// <summary>The caller lacks authentication.</summary>
        public const string Unauthenticated = "UNAUTHENTICATED";

        /// <summary>A quota or rate limit is exceeded.</summary>
        public const string ResourceExhausted = "RESOURCE_EXHAUSTED";

        /// <summary>An internal server error occurred.</summary>
        public const string Internal = "INTERNAL";
    }

    #endregion

    #region Nested type: ErrorReasons

    /// <summary>
    ///     Domain-specific <see cref="Errors.ErrorInfoDetail.Reason" /> identifiers attached
    ///     by the framework's named exceptions. These are intentionally more specific than
    ///     <see cref="ErrorCodes" />, which carry the top-level <c>google.rpc.Code</c> name.
    /// </summary>
    /// <remarks>
    ///     Per <seealso href="https://google.aip.dev/193">AIP-193</seealso> and
    ///     <see href="https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto">
    ///     google/rpc/error_details.proto</see>, <c>ErrorInfo.reason</c> exists to disambiguate
    ///     beyond the ~20 top-level Codes, so a NOT_FOUND status pairs with a more specific
    ///     reason such as <c>RESOURCE_NOT_FOUND</c>. Throw sites with finer context should
    ///     supply a still more specific reason via <c>Details = [new ErrorInfoDetail { Reason = "..." }]</c>.
    /// </remarks>
    public static class ErrorReasons
    {
        /// <summary>Default reason for a missing resource (Status NOT_FOUND).</summary>
        public const string ResourceNotFound = "RESOURCE_NOT_FOUND";

        /// <summary>Default reason for a conflicting create (Status ALREADY_EXISTS).</summary>
        public const string ResourceAlreadyExists = "RESOURCE_ALREADY_EXISTS";

        /// <summary>Default reason when system state blocks the operation (Status FAILED_PRECONDITION).</summary>
        public const string PreconditionNotSatisfied = "PRECONDITION_NOT_SATISFIED";

        /// <summary>Default reason for malformed request arguments (Status INVALID_ARGUMENT).</summary>
        public const string InvalidArgumentValue = "INVALID_ARGUMENT_VALUE";

        /// <summary>Default reason for field-level validation failures (Status INVALID_ARGUMENT).</summary>
        public const string ValidationFailed = "VALIDATION_FAILED";

        /// <summary>Default reason when tenant resolution fails (Status FAILED_PRECONDITION).</summary>
        public const string TenantResolutionFailed = "TENANT_RESOLUTION_FAILED";

        /// <summary>Default reason for missing or invalid credentials (Status UNAUTHENTICATED).</summary>
        public const string CredentialsMissingOrInvalid = "CREDENTIALS_MISSING_OR_INVALID";

        /// <summary>Default reason for permission rejection (Status PERMISSION_DENIED).</summary>
        public const string InsufficientPermission = "INSUFFICIENT_PERMISSION";

        /// <summary>Default reason when quota or rate limit is exceeded (Status RESOURCE_EXHAUSTED).</summary>
        public const string QuotaExceeded = "QUOTA_EXCEEDED";

        /// <summary>Optimistic-concurrency conflict (Status ABORTED).</summary>
        public const string ConcurrencyMismatch = "CONCURRENCY_MISMATCH";

        /// <summary>The named token does not exist on the addressed process (Status NOT_FOUND).</summary>
        public const string ProcessTokenNotFound = "PROCESS_TOKEN_NOT_FOUND";

        /// <summary>The addressed token is suspended, terminal, or otherwise not ready to receive the operation (Status FAILED_PRECONDITION).</summary>
        public const string ProcessTokenNotReady = "PROCESS_TOKEN_NOT_READY";

        /// <summary>The process has more than one ready token and the caller did not disambiguate (Status FAILED_PRECONDITION).</summary>
        public const string ProcessTokenAmbiguous = "PROCESS_TOKEN_AMBIGUOUS";

        /// <summary>A registered process references a BPMN-only AST node that the state-machine engine cannot run (Status FAILED_PRECONDITION).</summary>
        public const string StateMachineRequiresBpmnEngine = "STATE_MACHINE_REQUIRES_BPMN_ENGINE";
    }

    #endregion

    #region Nested type: IdentityClaims

    /// <summary>
    ///     Claim names that identify the authenticated principal. The Identity, Authorization and
    ///     Resource layers all read these, which is why they sit here rather than in one domain's
    ///     Skeleton. Claim names consumed only by the OAuth 2.0 / OpenID Connect server live in
    ///     <c>Schemata.Authorization.Skeleton.AuthorizationConstants.Claims</c>.
    /// </summary>
    public static class IdentityClaims
    {
        /// <summary>End-user email address claim.</summary>
        public const string Email             = "email";

        /// <summary>End-user preferred username claim.</summary>
        public const string PreferredUsername = "preferred_username";

        /// <summary>Role claim.</summary>
        public const string Role              = "role";

        /// <summary>Security stamp claim.</summary>
        public const string SecurityStamp     = "security_stamp";

        /// <summary>Subject claim.</summary>
        public const string Subject           = "sub";
    }

    #endregion

    #region Nested type: Keys

    /// <summary>
    ///     Option keys and cache keys.
    /// </summary>
    public static class Keys
    {
        /// <summary>Key for the features dictionary in SchemataOptions.</summary>
        public const string Features = "Features";

        /// <summary>Key for the modular modules list in configuration.</summary>
        public const string ModularModules = "Modular:Modules";

        /// <summary>Key for Authorization.</summary>
        public const string Authorization = "authorization";

        /// <summary>Key for Entity.</summary>
        public const string Entity = "entity";

        /// <summary>Key for Resource.</summary>
        public const string Resource = "resource";

        /// <summary>Key for Tenancy.</summary>
        public const string Tenancy = "tenancy";
    }

    #endregion

    #region Nested type: Orders

    /// <summary>
    ///     Well-known ordering constants for feature and advisor pipeline sequencing.
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

    #region Nested type: PreconditionSubjects

    /// <summary>
    ///     Well-known precondition subjects.
    /// </summary>
    public static class PreconditionSubjects
    {
        /// <summary>The request itself is the subject.</summary>
        public const string Request = "request";

        /// <summary>The resource is soft-deleted and blocks the update path.</summary>
        public const string SoftDeleted = "SOFT_DELETED";

        /// <summary>The resource is not soft-deleted and blocks the expunge path (AIP-164).</summary>
        public const string StateNotDeleted = "STATE_NOT_DELETED";

        /// <summary>The resource is not soft-deleted and blocks the undelete path (AIP-164).</summary>
        public const string NotSoftDeleted = "NOT_SOFT_DELETED";

        /// <summary>The supplied entity tag does not match the stored value (AIP-154).</summary>
        public const string EtagMismatch = "ETAG_MISMATCH";
    }

    #endregion

    #region Nested type: Principals

    /// <summary>
    ///     Caller identifiers used where no authenticated principal is available.
    /// </summary>
    public static class Principals
    {
        /// <summary>The anonymous caller identifier.</summary>
        public const string Anonymous = "anonymous";
    }

    #endregion

    #region Nested type: Verbs

    /// <summary>
    ///     Custom-method verbs declared by Schemata packages, rendered as the
    ///     <c>:{verb}</c> HTTP suffix and the <c>{Verb}{Singular}</c> gRPC RPC name per
    ///     <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>.
    /// </summary>
    public static class Verbs
    {
        /// <summary>Cancels a long-running operation.</summary>
        public const string Cancel = "cancel";

        /// <summary>Physically removes a soft-deleted resource, per AIP-164.</summary>
        public const string Expunge = "expunge";

        /// <summary>Generates a report snapshot or inline result.</summary>
        public const string Generate = "generate";

        /// <summary>Deletes resources matching a filter, per AIP-165.</summary>
        public const string Purge = "purge";

        /// <summary>Reads a page of report snapshot rows.</summary>
        public const string Read = "read";

        /// <summary>Triggers a job, per AIP-152.</summary>
        public const string Run = "run";

        /// <summary>Restores a soft-deleted resource, per AIP-164.</summary>
        public const string Undelete = "undelete";

        /// <summary>
        ///     Waits for a long-running operation to reach a terminal state, mirroring
        ///     <c>WaitOperation</c> on the <c>google.longrunning.Operations</c> service.
        /// </summary>
        public const string Wait = "wait";
    }

    #endregion

    #region Nested type: Wildcards

    /// <summary>
    ///     Wildcard tokens shared by field masks and filters.
    /// </summary>
    public static class Wildcards
    {
        /// <summary>Matches every field or every resource.</summary>
        public const string Any = "*";
    }

    #endregion
}
