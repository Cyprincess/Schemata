using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>
///     Hashes and verifies presented credentials against stored security rows.
///     Password hashing is keyed: hosts register a password hasher for
///     <see cref="SchemataSecurity" /> under <see cref="SecurityConstants.Algorithms" />
///     values (bcrypt, argon2id, …) and the verifier resolves by algorithm; keys without a
///     specific registration fall through to the default hasher registration.
/// </summary>
public interface ISecretVerifier
{
    /// <summary>
    ///     Hashes a presented plaintext for storage in a Kind=password row.
    ///     The returned value goes to <see cref="SchemataSecurity.Value" />; the algorithm
    ///     identifier goes to <see cref="SchemataSecurity.Algorithm" />. The hasher is
    ///     resolved by <paramref name="algorithm" />; <see langword="null" /> selects the
    ///     default (PBKDF2).
    /// </summary>
    /// <param name="presented">Plaintext as presented by the user.</param>
    /// <param name="algorithm">Hash format constant; <see langword="null" /> for the default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The storable hash string.</returns>
    Task<string> HashAsync(string presented, string? algorithm = null, CancellationToken ct = default);

    /// <summary>
    ///     Verifies a presented plaintext against a stored row. Kind=password rows verify
    ///     through the hasher resolved by the row's <see cref="SchemataSecurity.Algorithm" />
    ///     (default PBKDF2); a format the resolved hasher does not understand verifies as
    ///     failed rather than throwing. Kind=secret rows compare the stored plaintext in
    ///     fixed time. Other kinds have no presentation-verification semantics.
    /// </summary>
    /// <param name="stored">Stored row carrying the material to verify against.</param>
    /// <param name="presented">Plaintext as presented by the user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true" /> when the plaintext matches; otherwise, <see langword="false" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The row's kind has no presentation-verification semantics.
    /// </exception>
    Task<bool> VerifyAsync(SchemataSecurity stored, string presented, CancellationToken ct = default);
}
