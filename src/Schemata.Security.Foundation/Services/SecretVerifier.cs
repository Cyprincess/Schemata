using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Services;

/// <summary>
///     Default implementation of <see cref="ISecretVerifier" />. Kind=password rows hash and
///     verify through keyed <c>IPasswordHasher&lt;SchemataSecurity&gt;</c> registrations keyed by
///     <see cref="SchemataSecurity.Algorithm" /> (default PBKDF2); keys without a specific
///     registration resolve to the default hasher, and whether an unsupported format throws or
///     verifies as failed is that hasher's own contract. Kind=secret rows compare the stored
///     plaintext material in fixed time.
/// </summary>
public class SecretVerifier : ISecretVerifier
{
    private readonly IServiceProvider _services;

    public SecretVerifier(IServiceProvider services) {
        _services = services;
    }

    #region ISecretVerifier Members

    public Task<string> HashAsync(string presented, string? algorithm = null, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(Hasher(algorithm).HashPassword(null!, presented));
    }

    public Task<bool> VerifyAsync(SchemataSecurity stored, string presented, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(stored.Kind switch {
            SecurityConstants.Kinds.Password => VerifyPassword(stored, presented),
            SecurityConstants.Kinds.Secret   => VerifySecret(stored, presented),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stored), stored.Kind, "The security row kind has no presentation-verification semantics.")
        });
    }

    #endregion

    private bool VerifyPassword(SchemataSecurity stored, string presented) {
        if (string.IsNullOrWhiteSpace(stored.Value) || string.IsNullOrWhiteSpace(presented)) {
            return false;
        }

        return Hasher(stored.Algorithm).VerifyHashedPassword(null!, stored.Value, presented)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    private bool VerifySecret(SchemataSecurity stored, string presented) {
        if (string.IsNullOrWhiteSpace(stored.Value) || string.IsNullOrWhiteSpace(presented)) {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored.Value), Encoding.UTF8.GetBytes(presented));
    }

    private IPasswordHasher<SchemataSecurity> Hasher(string? algorithm) {
        return _services.GetRequiredKeyedService<IPasswordHasher<SchemataSecurity>>(
            algorithm ?? SecurityConstants.Algorithms.Pbkdf2);
    }
}
