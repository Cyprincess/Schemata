namespace Schemata.Insight.Skeleton.Models;

/// <summary>The kind of join between two source aliases.</summary>
public enum JoinKind
{
    Unspecified,
    Inner,
    Left,
    Right,
    Full,
}