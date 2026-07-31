using System.Collections.Generic;

namespace Schemata.Mapping.Mapster.Integration.Tests.Fixtures;

public sealed record RecordDestination(
    string?       Description,
    decimal       Rate,
    int           Count,
    bool          Active,
    List<string>? Tags,
    string?       Note);
