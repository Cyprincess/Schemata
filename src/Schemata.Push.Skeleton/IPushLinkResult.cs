using System.Collections.Generic;

namespace Schemata.Push.Skeleton;

public interface IPushLinkResult
{
    IEnumerable<string> GetLinkProviders();
}
