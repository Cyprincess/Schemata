namespace Schemata.Push.Skeleton;

/// <summary>Push domain constants.</summary>
public static class PushConstants
{
    /// <summary>Default handler key used when no explicit key is registered.</summary>
    public static class Handlers
    {
        /// <summary>The default handler key.</summary>
        public const string Default = "default";
    }

    /// <summary>
    ///     HTTP paths owned by the Push management surface.
    /// </summary>
    public static class Routes
    {
        /// <summary>The Push management controller base path.</summary>
        public const string Base = "/v1/push";

        /// <summary>The subscription collection segment.</summary>
        public const string Subscriptions = "subscriptions";

        /// <summary>The subscription collection path.</summary>
        public const string SubscriptionsPath = Base + "/" + Subscriptions;
    }
}
