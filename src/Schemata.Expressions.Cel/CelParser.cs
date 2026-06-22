using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Parlot;
using Parlot.Fluent;
using Schemata.Expressions.Cel.Expressions;

namespace Schemata.Expressions.Cel;

/// <summary>
///     Provides a parser for Common Expression Language syntax.
/// </summary>
public static class CelParser
{
    /// <summary>
    ///     Parses CEL expressions.
    /// </summary>
    public static readonly Parser<CelNode> Expression;

    static CelParser() {
        var expression = Parsers.Deferred<CelNode>();

        var dot               = Parsers.Terms.Char('.');
        var comma             = Parsers.Terms.Char(',');
        var colon             = Parsers.Terms.Char(':');
        var question          = Parsers.Terms.Char('?');
        var lparen            = Parsers.Terms.Char('(');
        var rparen            = Parsers.Terms.Char(')');
        var lbracket          = Parsers.Terms.Char('[');
        var rbracket          = Parsers.Terms.Char(']');
        var lbrace            = Parsers.Terms.Char('{');
        var rbrace            = Parsers.Terms.Char('}');
        var bang              = Parsers.Terms.Char('!');
        var singleQuote       = Parsers.Terms.Char('\'');
        var doubleQuote       = Parsers.Terms.Char('"');
        var singleTripleQuote = Parsers.Terms.Text("'''");
        var doubleTripleQuote = Parsers.Terms.Text("\"\"\"");

        var identifier = Parsers.Terms.Pattern(c => Character.IsIdentifierPart(c) || c > 127)
                                .Then((_, s) => s.Span.ToString());

        var @true     = WithWordBoundary(Parsers.Terms.Text("true", true)).Then<CelNode>(_ => new CelConstant(true));
        var @false    = WithWordBoundary(Parsers.Terms.Text("false", true)).Then<CelNode>(_ => new CelConstant(false));
        var @null     = WithWordBoundary(Parsers.Terms.Text("null", true)).Then<CelNode>(_ => new CelConstant(null));
        var hexDigits = Parsers.Terms.Pattern(IsHexDigit);
        var digits    = Parsers.Terms.Pattern(char.IsDigit);
        var sign      = Parsers.ZeroOrOne(Parsers.Terms.Char('+').Or(Parsers.Terms.Char('-')));
        var exponent  = Parsers.Terms.Char('e').Or(Parsers.Terms.Char('E')).And(sign).And(digits);
        var hexUint = Parsers.Terms.Text("0x", true)
                             .SkipAnd(hexDigits)
                             .And(Parsers.Terms.Text("u", true))
                             .Then<CelNode>(n => new CelConstant(ParseUIntHex(n.Item1.Span.ToString())));
        var decimalUint = digits.And(Parsers.Terms.Text("u", true))
                                .Then<CelNode>(n => new CelConstant(
                                                   ulong.Parse(n.Item1.Span.ToString(), CultureInfo.InvariantCulture)));
        var negativeHexInt = Parsers.Terms.Text("-0x", true)
                                    .SkipAnd(hexDigits)
                                    .Then<CelNode>((_, n) => new CelConstant(-ParseIntHex(n.Span.ToString())));
        var hexInt = Parsers.Terms.Text("0x", true)
                            .SkipAnd(hexDigits)
                            .Then<CelNode>((_, n) => new CelConstant(ParseIntHex(n.Span.ToString())));
        var decimalWithDot
            = Parsers.Capture(
                sign.And(digits).And(Parsers.Terms.Char('.')).And(digits).And(Parsers.ZeroOrOne(exponent)));
        var decimalLeadingDot
            = Parsers.Capture(sign.And(Parsers.Terms.Char('.')).And(digits).And(Parsers.ZeroOrOne(exponent)));
        var decimalWithExponent = Parsers.Capture(sign.And(digits).And(exponent));
        var @double = decimalWithDot.Or(decimalLeadingDot).Or(decimalWithExponent)
                                     .Then<CelNode>((_, s) => new CelConstant(
                                                        double.Parse(s.Span.ToString(), CultureInfo.InvariantCulture)));
        var integer = Parsers.Terms.Integer().Then<CelNode>((_, n) => new CelConstant(n));
        var number  = hexUint.Or(decimalUint).Or(negativeHexInt).Or(hexInt).Or(@double).Or(integer);

        var singleQuoted  = Parsers.Between(singleQuote, QuotedContent('\''), singleQuote);
        var doubleQuoted  = Parsers.Between(doubleQuote, QuotedContent('"'), doubleQuote);
        var quotedLiteral = singleQuoted.Or(doubleQuoted);
        var singleTripleQuoted = Parsers.Between(singleTripleQuote,
                                                 Parsers.AnyCharBefore(singleTripleQuote, true, true),
                                                 singleTripleQuote);
        var doubleTripleQuoted = Parsers.Between(doubleTripleQuote,
                                                 Parsers.AnyCharBefore(doubleTripleQuote, true, true),
                                                 doubleTripleQuote);
        var tripleQuotedLiteral = singleTripleQuoted.Or(doubleTripleQuoted);

        var normalString = quotedLiteral.Then<CelNode>((_, s) => new CelConstant(DecodeString(s)));
        var rawString = Parsers.Terms.Text("r", true)
                               .SkipAnd(quotedLiteral)
                               .Then<CelNode>((_, s) => new CelConstant(s));
        var rawTripleString = Parsers.Terms.Text("r", true)
                                     .SkipAnd(tripleQuotedLiteral)
                                     .Then<CelNode>((_, s) => new CelConstant(s.Span.ToString()));
        var bytesString = Parsers.Terms.Text("b", true)
                                 .SkipAnd(quotedLiteral)
                                 .Then<CelNode>((_, s) => new CelConstant(DecodeBytes(s)));
        var text = rawTripleString.Or(rawString).Or(bytesString).Or(normalString);

        var has = WithWordBoundary(Parsers.Terms.Text("has"))
                 .SkipAnd(Parsers.Between(lparen, expression, rparen))
                 .Then<CelNode>(arg => new CelCall("has", [arg]));

        var arguments = Parsers.Between(lparen, Parsers.ZeroOrOne(Parsers.Separated(comma, expression)), rparen)
                               .Then(args => args ?? []);

        var call = identifier.And(arguments).Then<CelNode>(c => new CelCall(c.Item1, c.Item2));

        var list = Parsers.Between(lbracket, Parsers.ZeroOrOne(Parsers.Separated(comma, expression)), rbracket)
                          .Then<CelNode>(items => new CelList(items ?? []));

        var mapEntry = expression.And(colon.SkipAnd(expression))
                                 .Then(e => new KeyValuePair<CelNode, CelNode>(e.Item1, e.Item2));
        var map = Parsers.Between(lbrace, Parsers.ZeroOrOne(Parsers.Separated(comma, mapEntry)), rbrace)
                         .Then<CelNode>(entries => new CelMap(
                                            entries ?? []));

        var identifierNode = identifier.Then<CelNode>(name => new CelIdentifier(name));
        var composite      = Parsers.Between(lparen, expression, rparen);
        var atom = has.Or(call)
                      .Or(@true)
                      .Or(@false)
                      .Or(@null)
                      .Or(number)
                      .Or(text)
                      .Or(list)
                      .Or(map)
                      .Or(identifierNode)
                      .Or(composite);
        var memberSuffix = dot.SkipAnd(identifier.And(Parsers.ZeroOrOne(arguments)))
                              .Then<object>(s => s);
        var indexSuffix = Parsers.Between(lbracket, expression, rbracket)
                                 .Then<object>(i => i);
        var primary = atom.And(Parsers.ZeroOrMany(memberSuffix.Or(indexSuffix)))
                          .Then<CelNode>(p => BuildPostfix(p.Item1, p.Item2));

        var unaryMinus = Parsers.Terms.Char('-')
                                .When((ctx, _) => {
                                     var cursor = ctx.Scanner.Cursor;
                                     return cursor.Eof || !char.IsDigit(cursor.Current);
                                 });
        var unary = primary.Unary((bang, operand => new CelUnary("!", operand)),
                                  (unaryMinus, operand => new CelUnary("-", operand)));

        var multiplicative = unary.LeftAssociative(
            (Parsers.Terms.Text("*"), (left, right) => new CelBinary("*", left, right)),
            (Parsers.Terms.Text("/"), (left, right) => new CelBinary("/", left, right)),
            (Parsers.Terms.Text("%"), (left, right) => new CelBinary("%", left, right)));

        var additive = multiplicative.LeftAssociative(
            (Parsers.Terms.Text("+"), (left, right) => new CelBinary("+", left, right)),
            (Parsers.Terms.Text("-"), (left, right) => new CelBinary("-", left, right)));

        var comparisonOperator = Parsers.Terms.Text("==")
                                        .Or(Parsers.Terms.Text("!="))
                                        .Or(Parsers.Terms.Text("<="))
                                        .Or(Parsers.Terms.Text(">="))
                                        .Or(Parsers.Terms.Text("<"))
                                        .Or(Parsers.Terms.Text(">"))
                                        .Or(WithWordBoundary(Parsers.Terms.Text("in", true)));

        var comparison = additive.And(Parsers.ZeroOrOne(comparisonOperator.And(additive)))
                                 .Then<CelNode>(c => c.Item2.Item2 is null
                                                    ? c.Item1
                                                    : new CelBinary(c.Item2.Item1, c.Item1, c.Item2.Item2));

        var and = comparison.LeftAssociative((Parsers.Terms.Text("&&"),
                                              (left, right) => new CelBinary("&&", left, right)));

        var or = and.LeftAssociative((Parsers.Terms.Text("||"), (left, right) => new CelBinary("||", left, right)));

        var conditional = or.And(Parsers.ZeroOrOne(question.SkipAnd(expression).And(colon.SkipAnd(expression))))
                            .Then<CelNode>(c => c.Item2.Item2 is null
                                               ? c.Item1
                                               : new CelConditional(c.Item1, c.Item2.Item1, c.Item2.Item2));

        expression.Parser = conditional;
        Expression        = expression.Compile();
    }

    private static Parser<string> WithWordBoundary(Parser<string> parser) {
        return parser.When((ctx, _) => {
            var cursor = ctx.Scanner.Cursor;
            return cursor.Eof || !Character.IsIdentifierPart(cursor.Current);
        });
    }

    private static CelNode BuildPostfix(CelNode target, IReadOnlyList<object> suffixes) {
        var node = target;
        for (var i = 0; i < suffixes.Count; i++) {
            var suffix = suffixes[i];
            node = suffix switch {
                CelNode index => new CelIndex(node, index),
                ValueTuple<string, IReadOnlyList<CelNode>> member => member.Item2 is null
                    ? new CelMember(node, member.Item1)
                    : new CelMemberCall(node, member.Item1, member.Item2),
                var _ => node,
            };
        }

        return node;
    }

    // String literal content must preserve whitespace, so every sub-parser here uses Parsers.Literals
    // (no leading-whitespace skipping) rather than Parsers.Terms; the opening quote stays a Terms parser
    // so a string token may still be preceded by whitespace in an expression.
    private static Parser<string> QuotedContent(char quote) {
        var plain            = Parsers.Literals.Pattern(c => c != quote && c != '\\').Then((_, s) => s.Span.ToString());
        var escapedQuote     = Parsers.Literals.Text("\\" + quote);
        var escapedBackslash = Parsers.Literals.Text("\\\\");
        var escaped = escapedQuote.Or(escapedBackslash)
                                  .Or(Parsers.Capture(Parsers.Literals.Char('\\')
                                                             .And(Parsers.Literals.Pattern(c => c != quote && c != '\\')))
                                             .Then((_, s) => s.Span.ToString()));

        return Parsers.ZeroOrMany(escaped.Or(plain)).Then(parts => string.Concat(parts));
    }

    private static bool IsHexDigit(char c) {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    private static long ParseIntHex(string source) {
        return long.Parse(source, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static ulong ParseUIntHex(string source) {
        return ulong.Parse(source, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string DecodeString(string source) {
        var builder = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++) {
            var c = source[i];
            if (c != '\\' || i + 1 >= source.Length) {
                builder.Append(c);
                continue;
            }

            var escaped = source[++i];
            switch (escaped) {
                case 'a':
                    builder.Append('\a');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case '\\':
                case '\"':
                case '\'':
                    builder.Append(escaped);
                    break;
                case 'u':
                    builder.Append((char)int.Parse(source.Substring(i + 1, 4), NumberStyles.HexNumber,
                                                   CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'U':
                    builder.Append(char.ConvertFromUtf32(int.Parse(source.Substring(i + 1, 8), NumberStyles.HexNumber,
                                                                    CultureInfo.InvariantCulture)));
                    i += 8;
                    break;
                default:
                    if (escaped >= '0' && escaped <= '7') {
                        var octal = source.Substring(i, Math.Min(3, source.Length - i));
                        builder.Append((char)Convert.ToByte(octal, 8));
                        i += octal.Length - 1;
                    } else {
                        throw new ParseException($"Invalid string escape sequence '\\{escaped}'.", default);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static byte[] DecodeBytes(string source) {
        var bytes = new List<byte>(source.Length);
        for (var i = 0; i < source.Length; i++) {
            var c = source[i];
            if (c != '\\' || i + 1 >= source.Length) {
                bytes.AddRange(Encoding.UTF8.GetBytes([c]));
                continue;
            }

            var escaped = source[++i];
            switch (escaped) {
                case 'a':
                    bytes.Add((byte)'\a');
                    break;
                case 'b':
                    bytes.Add((byte)'\b');
                    break;
                case 'f':
                    bytes.Add((byte)'\f');
                    break;
                case 'n':
                    bytes.Add((byte)'\n');
                    break;
                case 'r':
                    bytes.Add((byte)'\r');
                    break;
                case 't':
                    bytes.Add((byte)'\t');
                    break;
                case 'v':
                    bytes.Add((byte)'\v');
                    break;
                case '\\':
                case '"':
                case '\'':
                    bytes.Add((byte)escaped);
                    break;
                case 'x':
                    bytes.Add(byte.Parse(source.Substring(i + 1, 2), NumberStyles.HexNumber,
                                         CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                default:
                    if (escaped >= '0' && escaped <= '7') {
                        var octal = source.Substring(i, Math.Min(3, source.Length - i));
                        bytes.Add(Convert.ToByte(octal, 8));
                        i += octal.Length - 1;
                    } else {
                        throw new ParseException($"Invalid bytes escape sequence '\\{escaped}'.", default);
                    }

                    break;
            }
        }

        return bytes.ToArray();
    }
}
