namespace Schemata.Report.Skeleton;

/// <summary>Describes how a report definition is supplied.</summary>
public enum ReportSourceKind
{
    /// <summary>A JSON-serialized insight query supplies the report definition.</summary>
    Expression,

    /// <summary>A keyed dependency-injected provider supplies the report definition.</summary>
    Program,
}
