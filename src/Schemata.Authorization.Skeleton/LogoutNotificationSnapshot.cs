using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton;

/// <summary>Prepared notifications independent of token rows removed during invalidation.</summary>
public sealed record LogoutNotificationSnapshot(
    IReadOnlyList<string>                   FrontChannelUris,
    IReadOnlyList<BackChannelLogoutMessage> BackChannelMessages
);