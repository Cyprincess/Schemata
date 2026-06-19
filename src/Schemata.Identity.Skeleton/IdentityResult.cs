namespace Schemata.Identity.Skeleton;

/// <summary>
///     Represents the outcome of an identity operation with an optional payload.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public sealed class IdentityResult<T>
{
    private IdentityResult(IdentityStatus status, T? data) {
        Status = status;
        Data   = data;
    }

    /// <summary>State of the identity operation result.</summary>
    public IdentityStatus Status { get; }

    /// <summary>Operation payload associated with the result.</summary>
    public T? Data { get; }

    /// <summary>
    ///     Creates a successful identity result.
    /// </summary>
    /// <param name="data">The payload associated with the result.</param>
    /// <returns>A successful identity result.</returns>
    public static IdentityResult<T> Success(T? data) { return new(IdentityStatus.Success, data); }

    /// <summary>
    ///     Creates an identity result that requires an additional challenge.
    /// </summary>
    /// <returns>A challenge identity result.</returns>
    public static IdentityResult<T> Challenge() { return new(IdentityStatus.Challenge, default); }
}
