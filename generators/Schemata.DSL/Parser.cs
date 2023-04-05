using System.IO;
using System.Threading.Tasks;
using Parlot;
using Schemata.DSL.Terms;

namespace Schemata.DSL;

public class Parser
{
    private readonly Scanner _scanner;

    private Parser(string text) {
        _scanner = new Scanner(text);
    }

    public static Parser Read(string text) {
        return new Parser(text);
    }

    public static async Task<Parser> ReadAsync(Stream stream) {
        using var reader = new StreamReader(stream);

        var text = await reader.ReadToEndAsync();

        return new Parser(text);
    }

    // Mark = {Namespace | Enum | Entity | Trait}
    // Namespace = "Namespace" WS QualifiedName
    // Entity = "Entity" WS Name [ [WS] : Name { [WS] , [WS] Name } ] [WS] LC [ Note | Enum | Trait | Object | Index | Use | Field ] RC
    // Trait = "Trait" WS Name [ [WS] : Name { [WS] , [WS] Name } ] [WS] LC [ Note | Use | Field ] RC
    // Field = Type [ [WS] ? ] WS Name [ [WS] LB [ Option { [WS] , [WS] Option } ] RB ] [ [WS] LC {Note | Property} RC ]
    // Type = "String" | "Text" | "Integer" | "Int" | "Int32" | "Int4" | "Long" | "Int64" | "Int8" | "BigInteger" | "BigInt" | "Float" | "Double" | "Decimal" | "Boolean" | "DateTime" | "Timestamp" | "Guid" | Name
    // Option = "Required" | "Unique" | "PrimaryKey" | "Primary Key" | "AutoIncrement" | "Auto Increment" | "BTree" | "B Tree" | "Hash"
    // Property = ("Default" | "Length" | "Precision" | "Algorithm" | Key) WS Value
    // Value = String | QuotedString | Number | Boolean | MultilineString | Null
    // Enum = "Enum" WS Name [WS] LC [EnumValue | Note] RC
    // EnumValue = Name [ [WS] = [WS] Value ] [ [WS] LC [Note] RC ]
    // Index = "Index" WS Name { WS Name } [ [WS] LB [ Option { [WS] , [WS] Option } ] RB ] [ [WS] LC Note RC]
    // Index.Option = "Unique" | "BTree" | "B Tree" | "Hash"
    // Note = "Note" WS Value
    // Use = "Use" WS QualifiedName { [WS] , [WS] QualifiedName }
    public Mark? Parse() {
        return Mark.Parse(_scanner);
    }
}
