using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>Identifies the user field selected for an identity operation.</summary>
public enum UserAliasType
{
    UserName,
    Email,
    PhoneNumber,
}

/// <summary>Carries the selected lookup value and the username assigned during registration.</summary>
public sealed record UserAliasSelection(UserAliasType Type, string Value, string RegistrationName);

/// <summary>Selects identity aliases without inferring their types from their contents.</summary>
public interface IUserAliasResolver
{
    UserAliasSelection ResolveRegistration(RegisterRequest request);
    UserAliasSelection ResolveLogin(LoginRequest request);
}
