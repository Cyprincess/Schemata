using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Tests.Conformance;

public sealed record CelSpecCase(
    string Suite,
    string Name,
    string Expression,
    IReadOnlyDictionary<string, object?> Bindings,
    CelSpecValue Expected
);
