using System.Collections.Generic;

namespace Schemata.Mapping.Mapster.Integration.Tests.Fixtures;

public class Source
{
    public string? Name { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }

    public Sex? Sex { get; set; }

    public string? Nickname { get; set; }

    public SourceProfile? Profile { get; set; }

    public SourceAddress? Address { get; set; }

    public SourceSettings? Settings { get; set; }

    public List<string>? Tags { get; set; }
}
