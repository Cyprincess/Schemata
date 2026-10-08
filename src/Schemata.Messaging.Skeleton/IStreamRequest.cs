namespace Schemata.Messaging.Skeleton;

/// <summary>A cold stream request. Payloads must remain unchanged while enumerations are active.</summary>
public interface IStreamRequest<out TItem>;
