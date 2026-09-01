namespace Schemata.Insight.Skeleton.Models;

/// <summary>The field types a response schema can describe.</summary>
public enum FieldType
{
    Unspecified,
    String,
    Int64,
    Double,
    Bool,
    Timestamp,
    Duration,
    Bytes,
    Object = 100,
}