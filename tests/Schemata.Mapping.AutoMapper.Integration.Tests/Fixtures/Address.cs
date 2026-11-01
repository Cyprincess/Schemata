namespace Schemata.Mapping.AutoMapper.Integration.Tests.Fixtures;

public sealed class SourceAddress
{
    public string? City { get; set; }

    public string? Zip { get; set; }
}

public sealed class DestinationAddress
{
    public string? City { get; set; }

    public string? Zip { get; set; }
}
