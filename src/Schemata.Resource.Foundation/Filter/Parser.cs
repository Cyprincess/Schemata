using System.Linq;
using Parlot;
using Parlot.Fluent;
using Schemata.Resource.Foundation.Filter.Operations;
using Schemata.Resource.Foundation.Filter.Terms;
using Schemata.Resource.Foundation.Filter.Values;

namespace Schemata.Resource.Foundation.Filter;

public class Parser
{
    public static readonly Parser<Expression> Expression;

    static Parser() {
        var expression = Parsers.Deferred<Expression>();

        var accessor = Parsers.Terms.Char('.');
        var comma    = Parsers.Terms.Char(',');
        var lparen   = Parsers.Terms.Char('(');
        var rparen   = Parsers.Terms.Char(')');

        var and = Parsers.Terms.Text("AND");
        var or  = Parsers.Terms.Text("OR");

        var not   = Parsers.Terms.Text("NOT");
        var minus = Parsers.Terms.Char('-');

        var number  = Parsers.Terms.Decimal(NumberOptions.AllowSign).Then(n => new Number(n));
        var integer = Parsers.Terms.Integer(NumberOptions.AllowSign).Then(i => new Integer(i));
        var truth = Parsers.Terms.Text("TRUE")
                           .Or(Parsers.Terms.Text("FALSE"))
                           .Then(t => new Truth(string.Equals("true", t)));
        var @null = Parsers.Terms.Text("NULL").Then(_ => new Null());
        var @string = Parsers.Not(and.Or(or).Or(not))
                             .SkipAnd(Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || !char.IsAscii(c)));
        var text = Parsers.Terms.String().Or(@string).Then(t => new Text(t.Span.ToString()));

        var le  = Parsers.Terms.Text(LessThanOrEqual.Name).Then(_ => new LessThanOrEqual());
        var lt  = Parsers.Terms.Char(LessThan.Char).Then(_ => new LessThan());
        var ge  = Parsers.Terms.Text(GreaterThanOrEqual.Name).Then(_ => new GreaterThanOrEqual());
        var gt  = Parsers.Terms.Char(GreaterThan.Char).Then(_ => new GreaterThan());
        var em  = Parsers.Terms.Text(ExactMatch.Name).Then(_ => new ExactMatch());
        var fm  = Parsers.Terms.Text(FuzzyMatch.Name).Then(_ => new FuzzyMatch());
        var pm  = Parsers.Terms.Text(PrefixMatch.Name).Then(_ => new PrefixMatch());
        var sm  = Parsers.Terms.Text(SuffixMatch.Name).Then(_ => new SuffixMatch());
        var ne  = Parsers.Terms.Text(NotEqual.Name).Then(_ => new NotEqual());
        var eq  = Parsers.Terms.Char(Equal.Char).Then(_ => new Equal());
        var has = Parsers.Terms.Char(Has.Char).Then(_ => new Has());

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
        var composite = Parsers.Between(lparen, expression, rparen);

        // comparator
        // : LESS_EQUALS      # <=
        // | LESS_THAN        # <
        // | GREATER_EQUALS   # >=
        // | GREATER_THAN     # >
        // | NOT_EQUALS       # !=
        // | EQUALS           # =
        // | HAS              # :
        // ;
        var comparator = le.Then<IBinary>(c => c)
                           .Or(lt.Then<IBinary>(c => c))
                           .Or(ge.Then<IBinary>(c => c))
                           .Or(gt.Then<IBinary>(c => c))
                           .Or(em.Then<IBinary>(c => c))
                           .Or(fm.Then<IBinary>(c => c))
                           .Or(pm.Then<IBinary>(c => c))
                           .Or(sm.Then<IBinary>(c => c))
                           .Or(ne.Then<IBinary>(c => c))
                           .Or(eq.Then<IBinary>(c => c))
                           .Or(has.Then<IBinary>(c => c));

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
        var member = value.And(Parsers.ZeroOrMany(accessor.SkipAnd(value))).Then(m => new Member(m.Item1, m.Item2));

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

        // name
        // : TEXT
        // | keyword
        // ;

        // function
        // : name {DOT name} LPAREN [argList] RPAREN
        // ;
        var function = member.And(Parsers.Between(lparen, Parsers.ZeroOrOne(args), rparen))
                             .Then(f => new Function(f.Item1, f.Item2));

        // comparable
        // : member
        // | function
        // ;
        comparable.Parser = function.Then<IComparable>(c => c).Or(member.Then<IComparable>(c => c));

        // restriction
        // : comparable [comparator arg]
        // ;
        var restriction = comparable.And(Parsers.ZeroOrOne(comparator.And(arg)))
                                    .Then(r => new Restriction(r.Item1, r.Item2));

        // simple
        // : restriction
        // | composite
        // ;
        var simple = restriction.Then<ISimple>(t => t).Or(composite.Then<ISimple>(t => t));

        // term
        // : [(NOT WS | MINUS)] simple
        // ;
        var term = Parsers.ZeroOrOne(not.Or(minus.Then<string>(c => "-")))
                          .And(simple)
                          .Then(c => new Term(c.Item1, c.Item2));

        // factor
        // : term {WS OR WS term}
        // ;
        var factor = term.And(Parsers.ZeroOrMany(or.SkipAnd(term))).Then(c => new Factor(c.Item1, c.Item2));

        // sequence
        // : factor {WS factor}
        // ;
        var sequence = Parsers.OneOrMany(factor).Then(c => new Sequence(c));

        // expression
        // : sequence {WS AND WS sequence}
        // ;
        expression.Parser = sequence.And(Parsers.ZeroOrMany(and.SkipAnd(sequence)))
                                    .Then(c => new Expression(c.Item1, c.Item2));

        // filter
        // : [expression]
        // ;
        Expression = expression.Compile();
    }
}
