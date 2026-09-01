namespace Schemata.Authorization.Foundation.Services;

/// <summary>Selects token issuance or authorization callback issuance.</summary>
public enum AuthorizationSignInResponseKind
{
    Token,
    Callback,
}