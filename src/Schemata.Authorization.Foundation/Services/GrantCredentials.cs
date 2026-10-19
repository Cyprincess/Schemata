using System;
using System.Text.Json;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Copies the credentials issued during a grant pipeline — the DPoP proof key binding and
///     the device secret — onto the sign-in properties the token issuer consumes.
/// </summary>
internal static class GrantCredentials
{
    /// <summary>
    ///     Attaches the DPoP binding and device secret issued during the grant pipeline to the
    ///     sign-in result the issuer reads. Only sign-in results carry credentials; a sign-in
    ///     result without properties cannot carry either, which is a pipeline defect rather than
    ///     a silent drop.
    /// </summary>
    public static void Attach(AuthorizationResult result, DpopBinding? binding, DeviceSecretIssuance? deviceSecret) {
        if (result.Status != AuthorizationStatus.SignIn
         || (binding is null && deviceSecret is null)) {
            return;
        }

        if (result.Properties is null) {
            throw new InvalidOperationException(
                "The sign-in result carries no properties to attach the DPoP binding or device secret to.");
        }

        if (binding is not null) {
            result.Properties[Properties.DpopJkt] = binding.Jkt;
        }

        if (deviceSecret is null) {
            return;
        }

        result.Properties[Properties.DeviceSecret] = deviceSecret.DeviceSecret;
        if (deviceSecret.PreparedToken is not null) {
            result.Properties[Properties.DeviceSecretToken] =
                JsonSerializer.Serialize(deviceSecret.PreparedToken, Common.SchemataJson.Default);
        }
    }
}
