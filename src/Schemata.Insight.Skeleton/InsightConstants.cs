namespace Schemata.Insight.Skeleton;

/// <summary>Constants used by the Insight domain.</summary>
public static class InsightConstants
{
    /// <summary>Well-known keys for Insight request-handler registrations.</summary>
    public static class Handlers
    {
        /// <summary>The Foundation handler used when no optional wrapper replaces the unkeyed alias.</summary>
        public const string Default = "default";
    }

    /// <summary>Operation names checked by Insight authorization.</summary>
    public static class Operations
    {
        /// <summary>The Insight query operation.</summary>
        public const string Insight = "Insight";
    }
}
