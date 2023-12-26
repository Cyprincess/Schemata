using System.Collections.Generic;
using Parlot;

namespace Schemata.DSL.Terms;

public class Function : TermBase, IValueTerm
{
    public List<IValueTerm>? Parameters { get; set; }

    #region IValueTerm Members

    public string Body { get; set; } = null!;

    #endregion

    // Function = Name [WS] LP [ [WS] Name { [WS] , [WS] Name } ] RP
    public static Function? Parse(Mark mark, Scanner scanner) {
        var position = scanner.Cursor.Position;

        if (!scanner.ReadIdentifier(out var name)) return null;

        var function = new Function { Body = name.GetText() };

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (!scanner.ReadChar('(')) {
            scanner.Cursor.ResetPosition(position);
            return null;
        }

        while (true) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            if (scanner.ReadChar(')')) break;

            function.Parameters ??= new List<IValueTerm>();

            var value = Value.Parse(mark, scanner);
            if (value is null) {
                throw new ParseException("Expected a parameter name or an expression", scanner.Cursor.Position);
            }

            if (value.Type == typeof(object)) {
                function.Parameters.Add(new Ref { Body = value.Body });
            } else {
                function.Parameters.Add(value);
            }

            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            scanner.ReadChar(',');
        }

        return function;
    }
}
