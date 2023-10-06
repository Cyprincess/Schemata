using Parlot;

namespace Schemata.DSL.Terms;

public class Property : TermBase
{
    private string _name = null!;

    public string Name
    {
        get => _name;
        set => _name = ToCamelCase(value);
    }

    public string Body { get; set; } = null!;

    // Property = Name WS Value
    public static Property? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadIdentifier(out var name)) return null;

        scanner.SkipWhiteSpace();

        var value = Value.Parse(mark, scanner);
        if (value == null) {
            throw new ParseException("Expected a value", scanner.Cursor.Position);
        }

        EnsureLineEnd(scanner, true);

        return new Property { Name = name.GetText(), Body = value.Body };
    }
}
