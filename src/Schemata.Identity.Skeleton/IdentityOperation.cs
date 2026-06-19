namespace Schemata.Identity.Skeleton;

/// <summary>
///     Identifies an identity operation handled by the identity advisor pipeline.
/// </summary>
public enum IdentityOperation
{
    Register,
    Login,
    Refresh,
    Profile,
    ChangeEmail,
    ChangePhone,
    ChangePassword,
    Forgot,
    Reset,
    Confirm,
    Code,
    Authenticator,
    Enroll,
    Downgrade,
}
