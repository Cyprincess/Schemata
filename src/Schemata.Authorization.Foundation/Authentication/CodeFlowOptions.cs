namespace Schemata.Authorization.Foundation.Authentication;

public sealed class CodeFlowOptions
{
    public bool RequirePkce { get; set; } = true;

    public bool RequirePkceS256 { get; set; } = true;

    public bool RequirePkceDowngradeProtection { get; set; } = true;

    public bool RequireCodeSingleUse { get; set; } = true;

    public bool RequireNonce { get; set; } = true;

    public bool RequireResponseModeSafety { get; set; } = true;

    public CodeFlowOptions RelaxPkce() {
        RequirePkce = false;
        return this;
    }

    public CodeFlowOptions RelaxPkceS256() {
        RequirePkceS256 = false;
        return this;
    }

    public CodeFlowOptions RelaxPkceDowngradeProtection() {
        RequirePkceDowngradeProtection = false;
        return this;
    }

    public CodeFlowOptions RelaxCodeSingleUse() {
        RequireCodeSingleUse = false;
        return this;
    }

    public CodeFlowOptions RelaxNonce() {
        RequireNonce = false;
        return this;
    }

    public CodeFlowOptions RelaxResponseModeSafety() {
        RequireResponseModeSafety = false;
        return this;
    }
}
