using System;
using System.Collections.Generic;
using System.Linq;
using Parlot;
using Parlot.Fluent;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars.Expressions;
using Schemata.Resource.Foundation.Grammars.Operations;
using Schemata.Resource.Foundation.Grammars.Values;

namespace Schemata.Resource.Foundation.Grammars;

/// <summary>
///     Compiled <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso> filter and
///     order-by parsers built on <see href="https://github.com/sebastienros/parlot">Parlot</see>.
///     The filter grammar handles sequences (AND), factors (OR), terms (NOT/-),
///     restrictions (comparisons), functions (<c>fn(args)</c>), and member access (<c>a.b.c</c>).
/// </summary>
public class Parser
{
    /// <summary>
    ///     The compiled AIP-160 filter expression parser.
    /// </summary>
    public static readonly Parser<Filter> Filter;

    /// <summary>
    ///     The compiled order-by parser producing a mapping of
    ///     <see cref="Member" /> → <see cref="Ordering" />.
    /// </summary>
    public static readonly Parser<Dictionary<Member, Ordering>> Order;

    static Parser() {
        var filter = Parsers.Deferred<Filter>();

        var accessor = Parsers.Terms.Char('.');
        var comma    = Parsers.Terms.Char(',');
        var lparen   = Parsers.Terms.Char('(');
        var rparen   = Parsers.Terms.Char(')');

        var and = WithWordBoundary(Parsers.Terms.Text("AND", true));
        var or  = WithWordBoundary(Parsers.Terms.Text("OR", true));

        var not   = WithWordBoundary(Parsers.Terms.Text("NOT", true)).Then(_ => "NOT");
        var minus = Parsers.Terms.Char('-');

        var number = Parsers.Terms.Decimal().Then((c, n) => new Number(c.Scanner.Cursor.Position, n));
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
        var @true = WithWordBoundary(Parsers.Terms.Text("TRUE", true))
           .Then((c, _) => new Truth(c.Scanner.Cursor.Position, true));
        var @false = WithWordBoundary(Parsers.Terms.Text("FALSE", true))
           .Then((c, _) => new Truth(c.Scanner.Cursor.Position, false));
        var truth = @true.Or(@false);
        var @null = WithWordBoundary(Parsers.Terms.Text("NULL", true))
           .Then((c, _) => new Null(c.Scanner.Cursor.Position));
        var @string = Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || !char.IsAscii(c))
                             .When((_, span) => {
                                  var s = span.Span.ToString();
                                  return !string.Equals(s, "AND", StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "OR", StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "NOT", StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(s, "NULL", StringComparison.OrdinalIgnoreCase);
                              });
        var unquoted = @string.Then((c,                t) => new Text(c.Scanner.Cursor.Position, t.Span.ToString()));
        var quoted   = Parsers.Terms.String().Then((c, t) => new Text(c.Scanner.Cursor.Position, t.Span.ToString()));

        var le = Parsers.Terms.Text(LessThanOrEqual.Name)
                        .Then((c, _) => new LessThanOrEqual(c.Scanner.Cursor.Position));
        var lt = Parsers.Terms.Char(LessThan.Char).Then((c, _) => new LessThan(c.Scanner.Cursor.Position));
        var ge = Parsers.Terms.Text(GreaterThanOrEqual.Name)
                        .Then((c, _) => new GreaterThanOrEqual(c.Scanner.Cursor.Position));
        var gt  = Parsers.Terms.Char(GreaterThan.Char).Then((c, _) => new GreaterThan(c.Scanner.Cursor.Position));
        var ne  = Parsers.Terms.Text(NotEqual.Name).Then((c,    _) => new NotEqual(c.Scanner.Cursor.Position));
        var eq  = Parsers.Terms.Char(Equal.Char).Then((c,       _) => new Equal(c.Scanner.Cursor.Position));
        var has = Parsers.Terms.Char(Has.Char).Then((c,         _) => new Has(c.Scanner.Cursor.Position));

        var value = integer.Then<IValue>(v => v)
                           .Or(number.Then<IValue>(v => v))
                           .Or(truth.Then<IValue>(v => v))
                           .Or(@null.Then<IValue>(v => v))
                           .Or(unquoted.Then<IValue>(v => v))
                           .Or(quoted.Then<IValue>(v => v));

        var composite = Parsers.Between(lparen, filter, rparen);

        var comparator = le.Then<IBinary>(b => b)
                           .Or(lt.Then<IBinary>(b => b))
                           .Or(ge.Then<IBinary>(b => b))
                           .Or(gt.Then<IBinary>(b => b))
                           .Or(ne.Then<IBinary>(b => b))
                           .Or(eq.Then<IBinary>(b => b))
                           .Or(has.Then<IBinary>(b => b));

        var keyword = Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || !char.IsAscii(c))
                             .When((_, span) => {
                                  var s = span.Span.ToString();
                                  return string.Equals(s, "AND", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(s, "OR", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(s, "NOT", StringComparison.OrdinalIgnoreCase);
                              })
                             .Then((c, k) => new Text(c.Scanner.Cursor.Position, k.Span.ToString()));

        var field = value.Or(keyword.Then<IValue>(v => v));

        var member = value.And(Parsers.ZeroOrMany(accessor.SkipAnd(field)))
                          .Then((c, m) => new Member(c.Scanner.Cursor.Position, m.Item1, m.Item2));

        var name = unquoted.Then<IValue>(v => v).Or(keyword.Then<IValue>(v => v));

        var path = name.And(Parsers.ZeroOrMany(accessor.SkipAnd(name)))
                       .Then((c, m) => new Member(c.Scanner.Cursor.Position, m.Item1, m.Item2));

        var comparable = Parsers.Deferred<IComparableArg>();

        var arg = comparable.Then<IArg>(a => a).Or(composite.Then<IArg>(a => a));

        var args = arg.And(Parsers.ZeroOrMany(comma.SkipAnd(arg)))
                      .Then(a => a.Item2.Prepend(a.Item1).ToArray());

        var function = path.And(Parsers.Between(lparen, Parsers.ZeroOrOne(args), rparen))
                           .Then((c, f) => new Function(c.Scanner.Cursor.Position, f.Item1, f.Item2));

        comparable.Parser = function.Then<IComparableArg>(c => c).Or(member.Then<IComparableArg>(c => c));

        var restriction = comparable.And(Parsers.ZeroOrOne(comparator.And(arg)))
                                    .Then((c, r) => new Restriction(c.Scanner.Cursor.Position, r.Item1, r.Item2));

        var simple = restriction.Then<ISimple>(s => s).Or(composite.Then<ISimple>(s => s));

        var term = Parsers.ZeroOrOne(not.Or(minus.Then<string>(_ => "-")))
                          .And(simple)
                          .Then((c, t) => new Term(c.Scanner.Cursor.Position, t.Item1, t.Item2));

        var factor = term.And(Parsers.ZeroOrMany(or.SkipAnd(term)))
                         .Then((c, f) => new Factor(c.Scanner.Cursor.Position, f.Item1, f.Item2));

        var sequence = Parsers.OneOrMany(factor).Then((c, s) => new Sequence(c.Scanner.Cursor.Position, s));

        filter.Parser = sequence.And(Parsers.ZeroOrMany(and.SkipAnd(sequence)))
                                .Then((c, f) => new Filter(c.Scanner.Cursor.Position, f.Item1, f.Item2));

        Filter = filter.Compile();

        var asc  = WithWordBoundary(Parsers.Terms.Text("ASC", true)).Then(_ => Ordering.Ascending);
        var desc = WithWordBoundary(Parsers.Terms.Text("DESC", true)).Then(_ => Ordering.Descending);

        Order = Parsers.Separated(comma, member.And(Parsers.ZeroOrOne(asc.Or(desc))))
                       .Then(o => o.ToDictionary(kv => kv.Item1, kv => kv.Item2))
                       .Compile();
    }

    private static Parser<string> WithWordBoundary(Parser<string> parser) {
        return parser.When((ctx, _) => {
            var cursor = ctx.Scanner.Cursor;
            return cursor.Eof || !Character.IsIdentifierPart(cursor.Current);
        });
    }
}
