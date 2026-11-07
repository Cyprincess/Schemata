using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Schemata.Resource.Foundation.Models;

namespace Schemata.Resource.Foundation.Linking;

public sealed class ResourceLinkPolicy(
    IDataProtectionProvider protection, IOptions<SchemataResourceOptions> options) : IResourceLinkPolicy
{
    private readonly IDataProtector _protector = protection.CreateProtector(PageToken.ProtectionPurpose);

    public LinkPage? ReadPage(string? token) => PageToken.FromString(token, _protector) is { } page && token is not null
        ? new(page.PageSize, page.Skip, token) : null;

    public string? WritePage(LinkPage basis, int skip) {
        if (PageToken.FromString(basis.Token, _protector) is not { } page) return null;
        page.Skip = skip;
        return page.ToString(_protector);
    }

    public TotalSizeMode EffectiveTotalSize(ResourceRegistration? resource) {
        var mode = resource?.TotalSize ?? TotalSizeMode.Default;
        if (mode != TotalSizeMode.Default) return mode;
        return options.Value.TotalSize is TotalSizeMode.Default ? TotalSizeMode.Exact : options.Value.TotalSize;
    }
}
