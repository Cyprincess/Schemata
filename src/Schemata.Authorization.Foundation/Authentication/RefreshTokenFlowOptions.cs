namespace Schemata.Authorization.Foundation.Authentication;

public sealed class RefreshTokenFlowOptions
{
    public bool RequireRefreshTokenRotation { get; set; } = true;

    public RefreshTokenFlowOptions RelaxRefreshTokenRotation() {
        RequireRefreshTokenRotation = false;
        return this;
    }
}
