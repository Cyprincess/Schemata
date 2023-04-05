using System.Collections.Generic;
using Parlot;

namespace Schemata.DSL.Terms;

public class Function : ValueTermBase
{
    public List<string>? Parameters { get; set; }

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

            if (!scanner.ReadIdentifier(out var parameter)) {
                throw new ParseException("Expected a parameter name", scanner.Cursor.Position);
            }

            function.Parameters ??= new List<string>();
            function.Parameters.Add(parameter.GetText());

            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            scanner.ReadChar(',');
        }

        return function;
    }
}
