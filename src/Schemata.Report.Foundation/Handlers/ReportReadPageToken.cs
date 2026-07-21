using System;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Report.Foundation;

internal readonly record struct ReportReadPageToken(int ChunkIndex, int Offset)
{
    internal static string Encode(int chunkIndex, int offset) {
        var bytes = new byte[sizeof(int) * 2];
        BitConverter.GetBytes(chunkIndex).CopyTo(bytes, 0);
        BitConverter.GetBytes(offset).CopyTo(bytes, sizeof(int));
        return Convert.ToBase64String(bytes);
    }

    internal static ReportReadPageToken Decode(string token) {
        try {
            var bytes = Convert.FromBase64String(token);
            if (bytes.Length == sizeof(int) * 2) {
                var chunkIndex = BitConverter.ToInt32(bytes, 0);
                var offset     = BitConverter.ToInt32(bytes, sizeof(int));
                if (chunkIndex >= 0 && offset >= 0) {
                    return new(chunkIndex, offset);
                }
            }
        } catch (FormatException ex) {
            throw new InvalidArgumentException(message: $"Invalid page token '{token}': {ex.Message}");
        }

        throw new InvalidArgumentException(message: "Invalid page token.");
    }
}
