using System;
using ProtoBuf;
using ProtoBuf.Meta;
using ProtoBuf.WellKnownTypes;
using ProtoBuf.Serializers;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Grpc;

// The dependency's Duration conversion truncates sub-tick nanos and normalizes malformed values.
// Read the original fields before converting so the binding boundary can reject lossy input.
public sealed class PushOptionsSerializer : ISerializer<SendPushOptions>
{
    private static readonly ISerializer<Duration> DurationSerializer = TypeModel.GetInbuiltSerializer<Duration>(CompatibilityLevel.Level300, DataFormat.Default);

    public SerializerFeatures Features => SerializerFeatures.WireTypeString | SerializerFeatures.CategoryMessage;

    public SendPushOptions Read(ref ProtoReader.State state, SendPushOptions value) {
        value ??= new();
        int field;
        while ((field = state.ReadFieldHeader()) > 0) {
            switch (field) {
                case 1:
                    value.Priority = (PushPriority)state.ReadInt32();
                    break;
                case 2:
                    value.RawDuration = state.ReadMessage(DurationSerializer.Features,
                        value.RawDuration ?? (value.TimeToLive is { } duration
                            ? new Duration(duration.Ticks / TimeSpan.TicksPerSecond, (int)(duration.Ticks % TimeSpan.TicksPerSecond * 100))
                            : default), DurationSerializer);
                    break;
                case 3: value.CollapseKey = state.ReadString(); break;
                case 4: value.DedupId = state.ReadString(); break;
                default: state.SkipField(); break;
            }
        }
        if (value.RawDuration is { } raw) {
            var seconds = raw.Seconds;
            var nanos = raw.Nanoseconds;
            value.InvalidDuration = nanos is < -999999999 or > 999999999 || nanos % 100 != 0
                || seconds > 0 && nanos < 0 || seconds < 0 && nanos > 0;
            value.TimeToLive = null;
            if (!value.InvalidDuration) {
                try {
                    value.TimeToLive = TimeSpan.FromTicks(checked(seconds * TimeSpan.TicksPerSecond + nanos / 100));
                } catch (OverflowException) {
                    value.InvalidDuration = true;
                }
            }
        }
        return value;
    }

    public void Write(ref ProtoWriter.State state, SendPushOptions value) {
        if (value.Priority is { } priority) {
            state.WriteFieldHeader(1, WireType.Varint);
            state.WriteInt32((int)priority);
        }
        if (value.TimeToLive is { } duration) {
            var raw = new Duration(duration.Ticks / TimeSpan.TicksPerSecond,
                (int)(duration.Ticks % TimeSpan.TicksPerSecond * 100));
            state.WriteMessage(2, DurationSerializer.Features, raw, DurationSerializer);
        }
        if (value.CollapseKey is { } collapseKey) {
            state.WriteFieldHeader(3, WireType.String);
            state.WriteString(collapseKey);
        }
        if (value.DedupId is { } dedupId) {
            state.WriteFieldHeader(4, WireType.String);
            state.WriteString(dedupId);
        }
    }
}
