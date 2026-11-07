namespace Schemata.Quota.Skeleton;

public static class QuotaConstants
{
    public static class Algorithms
    {
        public const string FixedWindow = "fixed-window";
        public const string SlidingWindow = "sliding-window";
        public const string TokenBucket = "token-bucket";
        public const string Concurrency = "concurrency";
    }

    public static class Jobs
    {
        public const string Expiry = "schemata.quota.expiry";
        public const string ReservationUidVariable = "reservation_uid";
    }
}
