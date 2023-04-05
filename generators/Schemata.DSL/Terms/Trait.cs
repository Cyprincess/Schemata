using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Trait : Entity
{
    // Trait = "Trait" WS Name [ [WS] : Name { [WS] , [WS] Name } ] [WS] LC [ Note | Use | Field ] RC
    public new static Trait? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Trait), InvariantCultureIgnoreCase)) return null;

        scanner.SkipWhiteSpace();

        return Parse<Trait>(mark, scanner);
    }

    protected override bool ParseEnum(Mark mark, Scanner scanner) {
        return false;
    }

    protected override bool ParseTrait(Mark mark, Scanner scanner) {
        return false;
    }

    protected override bool ParseObject(Mark mark, Scanner scanner) {
        return false;
    }

    protected override bool ParseIndex(Mark mark, Scanner scanner) {
        return false;
    }
}
