using Schemata.Abstractions.Resource;
using Schemata.Core.Building;

namespace Schemata.Core.Linking;

public sealed record LinkPage(int PageSize, int Skip, string Token);

public interface IResourceLinkPolicy
{
    LinkPage? ReadPage(string? token);
    string? WritePage(LinkPage basis, int skip);
    TotalSizeMode EffectiveTotalSize(ResourceRegistration? resource);
}
