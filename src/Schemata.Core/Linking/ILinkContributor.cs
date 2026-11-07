using System.Collections.Generic;

namespace Schemata.Core.Linking;

public interface ILinkContributor
{
    IEnumerable<SemanticLink> GetLinks(LinkContext context);
}

public static class LinkTransports
{
    public const string Http = "http";
    public const string Grpc = "grpc";
}
