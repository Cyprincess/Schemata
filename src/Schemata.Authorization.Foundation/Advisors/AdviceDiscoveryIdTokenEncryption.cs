using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Advertises JWE algorithms accepted by the ID-token response protector.</summary>
public sealed class AdviceDiscoveryIdTokenEncryption : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryBase.DefaultOrder + 1_500_000;

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, DiscoveryContext discovery, CancellationToken ct = default) {
        discovery.Document ??= new();
        discovery.Document.IdTokenEncryptionAlgValuesSupported = [
            EncryptionAlgorithms.RsaOaep,
        ];
        discovery.Document.IdTokenEncryptionEncValuesSupported = [
            ContentEncryptionAlgorithms.Aes128CbcHmacSha256,
            ContentEncryptionAlgorithms.Aes192CbcHmacSha384,
            ContentEncryptionAlgorithms.Aes256CbcHmacSha512,
        ];
        return Task.FromResult(AdviseResult.Continue);
    }
}
