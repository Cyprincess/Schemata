using System;
using System.Globalization;
using System.Text;
using Parlot;
using Schemata.Expressions.Cel.Expressions;

namespace Schemata.Expressions.Cel;

/// <summary>
///     Produces structural cache keys for trees containing immutable scalar literals.
///     Captured reference constants keep their consumer-owned identity and bypass structural caching.
///     The mutable <see cref="CelNode.Source" /> annotation does not contribute to the key.
/// </summary>
internal static class CelTreeFingerprint
{
    /// <summary>
    ///     Returns canonical text and whether all captured literals are safe to share.
    /// </summary>
    public static string Compute(CelNode node, out bool cacheable) {
        var builder = new StringBuilder();
        cacheable = true;
        Write(node, builder, ref cacheable);
        return builder.ToString();
    }

    private static void Write(CelNode node, StringBuilder builder, ref bool cacheable) {
        switch (node) {
            case CelConstant constant:
                builder.Append("(const ");
                if (constant.Value is null) {
                    builder.Append("null");
                    break;
                }

                builder.Append("of:");
                builder.Append(constant.Value.GetType().FullName ?? constant.Value.GetType().Name);
                builder.Append(' ');
                cacheable &= AppendValue(constant.Value, builder);
                break;
            case CelIdentifier identifier:
                builder.Append("(id ");
                Append(identifier.Name, builder);
                break;
            case CelBinary binary:
                builder.Append("(bin ");
                Append(binary.Operator, builder);
                Write(binary.Left, builder, ref cacheable);
                Write(binary.Right, builder, ref cacheable);
                break;
            case CelUnary unary:
                builder.Append("(un ");
                Append(unary.Operator, builder);
                Write(unary.Operand, builder, ref cacheable);
                break;
            case CelCall call:
                builder.Append("(call ");
                Append(call.Name, builder);
                foreach (var arg in call.Args) {
                    Write(arg, builder, ref cacheable);
                }

                break;
            case CelMember member:
                builder.Append("(member ");
                Write(member.Target, builder, ref cacheable);
                Append(member.Member, builder);
                break;
            case CelMemberCall call:
                builder.Append("(mcall ");
                Write(call.Target, builder, ref cacheable);
                Append(call.Name, builder);
                foreach (var arg in call.Args) {
                    Write(arg, builder, ref cacheable);
                }

                break;
            case CelIndex index:
                builder.Append("(index ");
                Write(index.Target, builder, ref cacheable);
                Write(index.Index, builder, ref cacheable);
                break;
            case CelList list:
                builder.Append("(list");
                foreach (var item in list.Items) {
                    Write(item, builder, ref cacheable);
                }

                break;
            case CelMap map:
                builder.Append("(map");
                foreach (var entry in map.Entries) {
                    builder.Append("(entry ");
                    Write(entry.Key, builder, ref cacheable);
                    Write(entry.Value, builder, ref cacheable);
                    builder.Append(')');
                }

                break;
            case CelConditional conditional:
                builder.Append("(cond ");
                Write(conditional.Condition, builder, ref cacheable);
                Write(conditional.WhenTrue, builder, ref cacheable);
                Write(conditional.WhenFalse, builder, ref cacheable);
                break;
            default:
                throw new ParseException("Unsupported CEL node.", default);
        }

        builder.Append(')');
    }

    private static bool AppendValue(object value, StringBuilder builder) {
        switch (value) {
            case string text:
                Append(text, builder);
                break;
            case float number:
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case double number:
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case bool truth:
                builder.Append(truth ? "true" : "false");
                break;
            case char character:
                builder.Append((int)character);
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
                builder.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                return false;
        }

        return true;
    }

    // Escape delimiters so literal content cannot impersonate structural syntax.
    private static void Append(string value, StringBuilder builder) {
        foreach (var c in value) {
            switch (c) {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '(':
                    builder.Append("\\(");
                    break;
                case ')':
                    builder.Append("\\)");
                    break;
                case ' ':
                    builder.Append("\\s");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }
}
