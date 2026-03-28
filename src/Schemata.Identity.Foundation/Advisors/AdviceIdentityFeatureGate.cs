using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Advisors;

public static class AdviceIdentityFeatureGate
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceIdentityFeatureGate<T>(IOptionsMonitor<SchemataIdentityOptions> options) : IIdentityRequestAdvisor<T>
{
    #region IIdentityRequestAdvisor<T> Members

    public int Order => AdviceIdentityFeatureGate.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T                 request,
        IdentityOperation op,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var allowed = op switch {
            IdentityOperation.Register                          => options.CurrentValue.AllowRegistration,
            IdentityOperation.Forgot or IdentityOperation.Reset => options.CurrentValue.AllowPasswordReset,
            IdentityOperation.ChangeEmail                       => options.CurrentValue.AllowEmailChange,
            IdentityOperation.ChangePhone                       => options.CurrentValue.AllowPhoneNumberChange,
            IdentityOperation.ChangePassword                    => options.CurrentValue.AllowPasswordChange,
            IdentityOperation.Confirm or IdentityOperation.Code => options.CurrentValue.AllowAccountConfirmation,
            IdentityOperation.Authenticator or IdentityOperation.Enroll or IdentityOperation.Downgrade => options.CurrentValue.AllowTwoFactorAuthentication,
            var _ => true,
        };

        if (!allowed) {
            throw new NotFoundException();
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
