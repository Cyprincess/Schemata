using Parlot;

namespace Schemata.DSL.Terms;

public class Property : TermBase
{
    public string Name { get; set; } = null!;

    public string Body { get; set; } = null!;

    // Property = Name WS Value
    public static Property? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadIdentifier(out var name)) return null;

        scanner.SkipWhiteSpace();

        var value = Value.Parse(mark, scanner);

        EnsureLineEnd(scanner, true);

        return new Property { Name = name.GetText(), Body = value.Body };
    }
}
