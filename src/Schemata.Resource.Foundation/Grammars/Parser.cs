using System.Collections.Generic;
using System.Linq;
using Parlot;
using Parlot.Fluent;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars.Operations;
using Schemata.Resource.Foundation.Grammars.Expressions;
using Schemata.Resource.Foundation.Grammars.Values;

namespace Schemata.Resource.Foundation.Grammars;

/// <summary>
/// Provides compiled Parlot parsers for the AIP-160 filter and order-by grammars.
/// </summary>
public class Parser
{
    /// <summary>
    /// Gets the compiled parser for AIP-160 filter expressions.
    /// </summary>
    public static readonly Parser<Filter> Filter;

    /// <summary>
    /// Gets the compiled parser for order-by clauses (comma-separated member/direction pairs).
    /// </summary>
    public static readonly Parser<Dictionary<Member, Ordering>> Order;

    private static Parser<string> WithWordBoundary(Parser<string> parser) =>
        parser.When((ctx, _) => {
            var cursor = ctx.Scanner.Cursor;
            return cursor.Eof || !Character.IsIdentifierPart(cursor.Current);
        });

    static Parser() {
        var filter = Parsers.Deferred<Filter>();

        var accessor = Parsers.Terms.Char('.');
        var comma    = Parsers.Terms.Char(',');
        var lparen   = Parsers.Terms.Char('(');
        var rparen   = Parsers.Terms.Char(')');

        var and = WithWordBoundary(Parsers.Terms.Text("AND", caseInsensitive: true));
        var or  = WithWordBoundary(Parsers.Terms.Text("OR",  caseInsensitive: true));

        var not   = WithWordBoundary(Parsers.Terms.Text("NOT", caseInsensitive: true))
                        .Then(_ => "NOT");
        var minus = Parsers.Terms.Char('-');

        var number = Parsers.Terms.Decimal()
                            .Then((c, n) => new Number(c.Scanner.Cursor.Position, n));
        var integer = Parsers.Terms.Integer()
                             .When((ctx, _) => {
                                  var cursor = ctx.Scanner.Cursor;
                                  if (cursor.Eof || cursor.Current != '.') return true;
                                  var saved = cursor.Position;
                                  cursor.Advance();
                                  var isDecimal = !cursor.Eof && char.IsDigit(cursor.Current);
                                  cursor.ResetPosition(saved);
                                  return !isDecimal;
                              })
                             .Then((c, i) => new Integer(c.Scanner.Cursor.Position, i));
        var @true = WithWordBoundary(Parsers.Terms.Text("TRUE", caseInsensitive: true))
                           .Then((c, _) => new Truth(c.Scanner.Cursor.Position, true));
        var @false = WithWordBoundary(Parsers.Terms.Text("FALSE", caseInsensitive: true))
                            .Then((c, _) => new Truth(c.Scanner.Cursor.Position, false));
        var truth = @true.Or(@false);
        var @null = WithWordBoundary(Parsers.Terms.Text("NULL", caseInsensitive: true))
                           .Then((c, _) => new Null(c.Scanner.Cursor.Position));
        var @string = Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || !char.IsAscii(c))
                             .When((ctx, span) => {
                                  var s = span.Span.ToString();
                                  return !string.Equals(s, "AND",   System.StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "OR",    System.StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "NOT",   System.StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "TRUE",  System.StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "FALSE", System.StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "NULL",  System.StringComparison.OrdinalIgnoreCase);
                              });
        var unquoted = @string
                                  .Then((c, t) => new Text(c.Scanner.Cursor.Position, t.Span.ToString()));
        var quoted = Parsers.Terms.String()
                                  .Then((c, t) => new Text(c.Scanner.Cursor.Position, t.Span.ToString()));

        var le = Parsers.Terms.Text(LessThanOrEqual.Name)
                        .Then((c, _) => new LessThanOrEqual(c.Scanner.Cursor.Position));
        var lt = Parsers.Terms.Char(LessThan.Char).Then((c, _) => new LessThan(c.Scanner.Cursor.Position));
        var ge = Parsers.Terms.Text(GreaterThanOrEqual.Name)
                        .Then((c, _) => new GreaterThanOrEqual(c.Scanner.Cursor.Position));
        var gt  = Parsers.Terms.Char(GreaterThan.Char).Then((c, _) => new GreaterThan(c.Scanner.Cursor.Position));
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
                           .Or(unquoted.Then<IValue>(v => v))
                           .Or(quoted.Then<IValue>(v => v));

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
                           .Or(ne.Then<IBinary>(b => b))
                           .Or(eq.Then<IBinary>(b => b))
                           .Or(has.Then<IBinary>(b => b));

        // keyword
        // : NOT
        // | AND
        // | OR
        // ;
        var keyword = Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || !char.IsAscii(c))
                             .When((ctx, span) => {
                                  var s = span.Span.ToString();
                                  return string.Equals(s, "AND", System.StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(s, "OR",  System.StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(s, "NOT", System.StringComparison.OrdinalIgnoreCase);
                              })
                             .Then((c, k) => new Text(c.Scanner.Cursor.Position, k.Span.ToString()));

        // field
        // : value
        // | keyword
        // ;
        var field = value.Or(keyword.Then<IValue>(v => v));

        // member
        // : value {DOT field}
        // ;
        var member = value.And(Parsers.ZeroOrMany(accessor.SkipAnd(field)))
                          .Then((c, m) => new Member(c.Scanner.Cursor.Position, m.Item1, m.Item2));

        // name: text or keyword (identifier-like tokens only, per EBNF)
        var name = unquoted.Then<IValue>(v => v).Or(keyword.Then<IValue>(v => v));

        // path: name { "." name } — restricted member for function paths
        var path = name.And(Parsers.ZeroOrMany(accessor.SkipAnd(name)))
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
        var function = path.And(Parsers.Between(lparen, Parsers.ZeroOrOne(args), rparen))
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

        // order
        // : { member [ASC | DESC] }
        // ;

        var asc  = WithWordBoundary(Parsers.Terms.Text("ASC",  caseInsensitive: true)).Then(_ => Ordering.Ascending);
        var desc = WithWordBoundary(Parsers.Terms.Text("DESC", caseInsensitive: true)).Then(_ => Ordering.Descending);

        Order = Parsers.Separated(comma, member.And(Parsers.ZeroOrOne(asc.Or(desc))))
                       .Then(o => o.ToDictionary(kv => kv.Item1, kv => kv.Item2))
                       .Compile();
    }
}
