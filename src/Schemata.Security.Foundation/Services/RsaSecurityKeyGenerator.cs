using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Services;

/// <summary>Generates 2048-bit RSA PKCS#8 credentials in the local process.</summary>
/// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
public sealed class RsaSecurityKeyGenerator<TSecurity> : ISecurityKeyGenerator<TSecurity>
    where TSecurity : SchemataSecurity, new()
{
    /// <inheritdoc />
    public Task<TSecurity> GenerateAsync(SecurityKeyGenerationContext context, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(context.Algorithm, SecurityConstants.Algorithms.Rsa, StringComparison.Ordinal)) {
            throw new NotSupportedException($"The RSA key generator cannot generate '{context.Algorithm}' material.");
        }

        using var rsa = RSA.Create(2048);
        var kid = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        return Task.FromResult(new TSecurity {
            Parent = context.Parent,
            Key = kid,
            Kind = SecurityConstants.Kinds.PrivateKey,
            Usage = context.Usage,
            Algorithm = context.Algorithm,
            Kid = kid,
            Value = rsa.ExportPkcs8PrivateKeyPem(),
            Status = SecurityConstants.Statuses.Staged,
        });
    }
}
