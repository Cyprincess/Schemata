using System.Linq;
using Parlot;
using Parlot.Fluent;
using Schemata.Resource.Foundation.Filters.Operations;
using Schemata.Resource.Foundation.Filters.Terms;
using Schemata.Resource.Foundation.Filters.Values;

namespace Schemata.Resource.Foundation.Filters;

public class Parser
{
    public static readonly Parser<Filter> Filter;

    static Parser() {
        var filter = Parsers.Deferred<Filter>();

        var accessor = Parsers.Terms.Char('.');
        var comma    = Parsers.Terms.Char(',');
        var lparen   = Parsers.Terms.Char('(');
        var rparen   = Parsers.Terms.Char(')');

        var and = Parsers.Terms.Text("AND");
        var or  = Parsers.Terms.Text("OR");

        var not   = Parsers.Terms.Text("NOT");
        var minus = Parsers.Terms.Char('-');

        var number = Parsers.Terms.Decimal(NumberOptions.AllowSign)
                            .Then((c, n) => new Number(c.Scanner.Cursor.Position, n));
        var integer = Parsers.Terms.Integer(NumberOptions.AllowSign)
                             .Then((c, i) => new Integer(c.Scanner.Cursor.Position, i));
        var truth = Parsers.Terms.Text("TRUE")
                           .Or(Parsers.Terms.Text("FALSE"))
                           .Then((c, t) => new Truth(c.Scanner.Cursor.Position, string.Equals("true", t)));
        var @null = Parsers.Terms.Text("NULL").Then((c, _) => new Null(c.Scanner.Cursor.Position));
        var @string = Parsers.Not(and.Or(or).Or(not))
                             .SkipAnd(Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || !char.IsAscii(c)));
        var text = Parsers.Terms.String()
                          .Or(@string)
                          .Then((c, t) => new Text(c.Scanner.Cursor.Position, t.Span.ToString()));

        var le = Parsers.Terms.Text(LessThanOrEqual.Name)
                        .Then((c, _) => new LessThanOrEqual(c.Scanner.Cursor.Position));
        var lt = Parsers.Terms.Char(LessThan.Char).Then((c, _) => new LessThan(c.Scanner.Cursor.Position));
        var ge = Parsers.Terms.Text(GreaterThanOrEqual.Name)
                        .Then((c, _) => new GreaterThanOrEqual(c.Scanner.Cursor.Position));
        var gt  = Parsers.Terms.Char(GreaterThan.Char).Then((c, _) => new GreaterThan(c.Scanner.Cursor.Position));
        var em  = Parsers.Terms.Text(ExactMatch.Name).Then((c,  _) => new ExactMatch(c.Scanner.Cursor.Position));
        var fm  = Parsers.Terms.Text(FuzzyMatch.Name).Then((c,  _) => new FuzzyMatch(c.Scanner.Cursor.Position));
        var pm  = Parsers.Terms.Text(PrefixMatch.Name).Then((c, _) => new PrefixMatch(c.Scanner.Cursor.Position));
        var sm  = Parsers.Terms.Text(SuffixMatch.Name).Then((c, _) => new SuffixMatch(c.Scanner.Cursor.Position));
        var ne  = Parsers.Terms.Text(NotEqual.Name).Then((c,    _) => new NotEqual(c.Scanner.Cursor.Position));
        var eq  = Parsers.Terms.Char(Equal.Char).Then((c,       _) => new Equal(c.Scanner.Cursor.Position));
        var has = Parsers.Terms.Char(Has.Char).Then((c,         _) => new Has(c.Scanner.Cursor.Position));

        // value
        // : TEXT
        // | STRING
        // ;
        var value = integer.Then<IValue>(v => v)
                           .Or(number.Then<IValue>(v => v))
                           .Or(truth.Then<IValue>(v => v))
                           .Or(@null.Then<IValue>(v => v))
                           .Or(text.Then<IValue>(v => v));

        // composite
        // : LPAREN expression RPAREN
        // ;
        var composite = Parsers.Between(lparen, filter, rparen);

        // comparator
        // : LESS_EQUALS      # <=
        // | LESS_THAN        # <
        // | GREATER_EQUALS   # >=
        // | GREATER_THAN     # >
        // | NOT_EQUALS       # !=
        // | EQUALS           # =
        // | HAS              # :
        // ;
        var comparator = le.Then<IBinary>(b => b)
                           .Or(lt.Then<IBinary>(b => b))
                           .Or(ge.Then<IBinary>(b => b))
                           .Or(gt.Then<IBinary>(b => b))
                           .Or(em.Then<IBinary>(b => b))
                           .Or(fm.Then<IBinary>(b => b))
                           .Or(pm.Then<IBinary>(b => b))
                           .Or(sm.Then<IBinary>(b => b))
                           .Or(ne.Then<IBinary>(b => b))
                           .Or(eq.Then<IBinary>(b => b))
                           .Or(has.Then<IBinary>(b => b));

        // keyword
        // : NOT
        // | AND
        // | OR
        // ;
        // field
        // : value
        // | keyword
        // ;

        // member
        // : value {DOT field}
        // ;
        var member = value.And(Parsers.ZeroOrMany(accessor.SkipAnd(value)))
                          .Then((c, m) => new Member(c.Scanner.Cursor.Position, m.Item1, m.Item2));

        var comparable = Parsers.Deferred<IComparable>();

        // arg
        // : comparable
        // | composite
        // ;
        var arg = comparable.Then<IArg>(a => a).Or(composite.Then<IArg>(a => a));

        // argList
        // : arg { COMMA arg}
        // ;
        var args = arg.And(Parsers.ZeroOrMany(comma.SkipAnd(arg)))
                      .Then(a => a.Item2?.Prepend(a.Item1).ToArray() ?? [a.Item1]);

        // function
        // : name {DOT name} LPAREN [argList] RPAREN
        // ;
        var function = member.And(Parsers.Between(lparen, Parsers.ZeroOrOne(args), rparen))
                             .Then((c, f) => new Function(c.Scanner.Cursor.Position, f.Item1, f.Item2));

        // comparable
        // : member
        // | function
        // ;
        comparable.Parser = function.Then<IComparable>(c => c).Or(member.Then<IComparable>(c => c));

        // restriction
        // : comparable [comparator arg]
        // ;
        var restriction = comparable.And(Parsers.ZeroOrOne(comparator.And(arg)))
                                    .Then((c, r) => new Restriction(c.Scanner.Cursor.Position, r.Item1, r.Item2));

        // simple
        // : restriction
        // | composite
        // ;
        var simple = restriction.Then<ISimple>(s => s).Or(composite.Then<ISimple>(s => s));

        // term
        // : [(NOT WS | MINUS)] simple
        // ;
        var term = Parsers.ZeroOrOne(not.Or(minus.Then<string>(_ => "-")))
                          .And(simple)
                          .Then((c, t) => new Term(c.Scanner.Cursor.Position, t.Item1, t.Item2));

        // factor
        // : term {WS OR WS term}
        // ;
        var factor = term.And(Parsers.ZeroOrMany(or.SkipAnd(term)))
                         .Then((c, f) => new Factor(c.Scanner.Cursor.Position, f.Item1, f.Item2));

        // sequence
        // : factor {WS factor}
        // ;
        var sequence = Parsers.OneOrMany(factor).Then((c, s) => new Sequence(c.Scanner.Cursor.Position, s));

        // expression
        // : sequence {WS AND WS sequence}
        // ;
        filter.Parser = sequence.And(Parsers.ZeroOrMany(and.SkipAnd(sequence)))
                                .Then((c, f) => new Filter(c.Scanner.Cursor.Position, f.Item1, f.Item2));

        // filter
        // : [expression]
        // ;
        Filter = filter.Compile();
    }
}
