namespace Schemata.DSL;

public static class Constants
{
    public static class Options
    {
        public const string AutoIncrement = "AutoIncrement";
        public const string PrimaryKey    = "PrimaryKey";

        public const string NotNull  = "NotNull";
        public const string Required = "Required";

        public const string Unique = "Unique";
        public const string BTree  = "BTree";
        public const string Hash   = "Hash";

        public const string Omit    = "Omit";
        public const string OmitAll = "OmitAll";
    }

    public static class Types
    {
        public const string String     = "String";
        public const string Text       = "Text";
        public const string Integer    = "Integer";
        public const string Int        = "Int";
        public const string Int32      = "Int32";
        public const string Int4       = "Int4";
        public const string Long       = "Long";
        public const string Int64      = "Int64";
        public const string Int8       = "Int8";
        public const string BigInteger = "BigInteger";
        public const string BigInt     = "BigInt";
        public const string Float      = "Float";
        public const string Double     = "Double";
        public const string Decimal    = "Decimal";
        public const string Boolean    = "Boolean";
        public const string DateTime   = "DateTime";
        public const string Timestamp  = "Timestamp";
        public const string Guid       = "Guid";
    }

    public static class Properties
    {
        public const string Default   = "Default";
        public const string Length    = "Length";
        public const string Precision = "Precision";
        public const string Algorithm = "Algorithm";
    }
}
