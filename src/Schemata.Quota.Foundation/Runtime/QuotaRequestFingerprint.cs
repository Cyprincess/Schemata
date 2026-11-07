using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Schemata.Quota.Skeleton;

namespace Schemata.Quota.Foundation.Runtime;

internal static class QuotaRequestFingerprint
{
    internal static string Compute(QuotaScope scope, QuotaSubject subject, IEnumerable<QuotaCharge> charges, DateTimeOffset? expiresAt) {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) {
            writer.Write(scope.Value);
            writer.Write(subject.Value);
            foreach (var charge in charges) {
                writer.Write(charge.Policy.Name);
                writer.Write(charge.Policy.Version);
                writer.Write(charge.Policy.Algorithm);
                writer.Write(charge.Policy.Limit);
                writer.Write(charge.Policy.Period.Ticks);
                writer.Write(charge.Policy.Burst);
                writer.Write(charge.Metric.Value);
                writer.Write(charge.Amount);
            }

            writer.Write(expiresAt.HasValue);
            if (expiresAt is { } expiry) writer.Write(expiry.UtcTicks);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
