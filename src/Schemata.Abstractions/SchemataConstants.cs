namespace Schemata.Abstractions;

public static class SchemataConstants
{
    public const string Schemata = "9049a32e-c96b-4e0e-ae34-c370c574f00d";

    #region Nested type: ErrorCodes

    public static class ErrorCodes
    {
        public const string Ok                 = "OK";
        public const string InvalidArgument    = "INVALID_ARGUMENT";
        public const string NotFound           = "NOT_FOUND";
        public const string PermissionDenied   = "PERMISSION_DENIED";
        public const string Aborted            = "ABORTED";
        public const string AlreadyExists      = "ALREADY_EXISTS";
        public const string FailedPrecondition = "FAILED_PRECONDITION";
        public const string Unauthenticated    = "UNAUTHENTICATED";
        public const string ResourceExhausted  = "RESOURCE_EXHAUSTED";
        public const string Internal           = "INTERNAL";
    }

    #endregion

    #region Nested type: ErrorReasons

    public static class ErrorReasons
    {
        public const string ConcurrencyMismatch = "CONCURRENCY_MISMATCH";
    }

    #endregion

    #region Nested type: FieldReasons

    public static class FieldReasons
    {
        public const string Required               = "REQUIRED";
        public const string InvalidPayload         = "INVALID_PAYLOAD";
        public const string InvalidFilter          = "INVALID_FILTER";
        public const string InvalidOrderBy         = "INVALID_ORDER_BY";
        public const string InvalidPageToken       = "INVALID_PAGE_TOKEN";
        public const string CrossParentUnsupported = "CROSS_PARENT_UNSUPPORTED";
    }

    #endregion

    #region Nested type: Options

    public static class Options
    {
        public const string Features       = "Features";
        public const string ModularModules = "Modular:Modules";
    }

    #endregion

    #region Nested type: Orders

    public static class Orders
    {
        public const int Max = 2_147_400_000;
    }

    #endregion

    #region Nested type: Parameters

    public static class Parameters
    {
        public const string EntityTag = "etag";
        public const string Type      = "@type";
    }

    #endregion

    #region Nested type: PreconditionSubjects

    public static class PreconditionSubjects
    {
        public const string Request = "request";
    }

    #endregion

    #region Nested type: PreconditionTypes

    public static class PreconditionTypes
    {
        public const string Tenant = "TENANT";
    }

    #endregion
}
