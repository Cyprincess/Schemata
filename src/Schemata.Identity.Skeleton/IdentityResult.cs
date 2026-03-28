namespace Schemata.Identity.Skeleton;

public sealed class IdentityResult<T>
{
    private IdentityResult(IdentityStatus status, T? data) {
        Status = status;
        Data   = data;
    }

    /// <summary>Whether the operation completed successfully, requires a challenge, or returned no data.</summary>
    public IdentityStatus Status { get; }

    /// <summary>Operation payload; null when Status is Challenge or Empty.</summary>
    public T? Data { get; }

    public static IdentityResult<T> Success(T? data) { return new(IdentityStatus.Success, data); }

    public static IdentityResult<T> Challenge() { return new(IdentityStatus.Challenge, default); }
}
