namespace Schemata.Report.Skeleton;

/// <summary>Report domain constants.</summary>
public static class ReportConstants
{
    /// <summary>Well-known request-handler registration keys.</summary>
    public static class Handlers
    {
        /// <summary>The Foundation handler used when no optional wrapper replaces the unkeyed alias.</summary>
        public const string Default = "default";
    }

    /// <summary>Advisor ordering anchors for the Report capability.</summary>
    public static class AdvisorOrders
    {
        /// <summary>Capability-state guard runs before security and persistence advisors.</summary>
        public const int CapabilityGuard = 0;
    }

    /// <summary>Well-known keyed-service slots for Report capability implementations.</summary>
    public static class Services
    {
        /// <summary>The Foundation default implementation a facade delegates to.</summary>
        public const string Default = "default";

        /// <summary>The explicitly selected custom implementation; the last builder selection wins.</summary>
        public const string Selected = "selected";
    }
}
