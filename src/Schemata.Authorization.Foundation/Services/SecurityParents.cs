using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Canonical parent addressing for security rows attached to authorization resources.
///     Security row parents are polymorphic canonical names; call sites address a parent
///     through this helper instead of interpolating the string form inline.
/// </summary>
public static class SecurityParents
{
    /// <summary>Parent of the security rows that belong to a registered client application.</summary>
    public static string Application(SchemataApplication app) {
        return $"applications/{app.ClientId}";
    }

    /// <summary>Parent of the security rows that belong to an issuer (its URI).</summary>
    public static string Issuer(string issuer) {
        return issuer;
    }
}
