using System.Collections.Generic;

namespace Schemata.Mapping.AutoMapper.Integration.Tests.Fixtures;

public class Destination
{
    public string? DisplayName { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }

    public string? Sex { get; set; }

    public string? Nickname { get; set; }

    public DestinationProfile? Profile { get; set; }

    public string? Title { get; set; }

    public DestinationAddress? Address { get; set; }

    public DestinationSettings? Settings { get; set; }

    public List<string>? Tags { get; set; }
}
