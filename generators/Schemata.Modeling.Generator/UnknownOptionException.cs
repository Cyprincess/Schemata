using System;

namespace Schemata.Modeling.Generator;

// Thrown by the parser when an option entry matches no known field, pointer, or
// view option. It is the only parser failure the generator boundary converts into
// a located SKM002 diagnostic; anything else escaping the parser is a generator bug.
internal sealed class UnknownOptionException(string kind, string name, int offset)
    : Exception($"Unknown {kind} option: {name}")
{
    public string Kind { get; } = kind;

    public string Name { get; } = name;

    public int Offset { get; } = offset;
}
