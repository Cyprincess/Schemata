using Parlot;

namespace Schemata.DSL.Terms;

public class Option : TermBase
{
    private string _name = null!;

    public string Name
    {
        get => _name;
        set
        {
            value = Utilities.ToCamelCase(value);
            _name = value switch {
                SkmConstants.Options.NotNull => SkmConstants.Options.Required,
                var _                        => value,
            };
        }
    }

    // Option = "Required" | "Unique" | "PrimaryKey" | "Primary Key" | "AutoIncrement" | "Auto Increment" | "BTree" | "B Tree" | "Hash" | "OmitAll" | "Omit All"
    public static Option? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadWhile(x => !Character.IsNewLine(x) && !x.IsStopWord(), out var name)) {
            throw new ParseException("Expected an option", scanner.Cursor.Position);
        }

        return new() { Name = name.GetText() };
    }
}
