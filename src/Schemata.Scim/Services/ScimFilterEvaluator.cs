using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Schemata.Scim.Models;
using static Schemata.Scim.ScimJson;

namespace Schemata.Scim.Services;

/// <summary>Evaluates schema-resolved SCIM filters against mapped JSON resources.</summary>
public static class ScimFilterEvaluator
{
    public static bool Matches(ScimFilterExpression filter, JsonObject resource) {
        return filter switch {
            ScimLogicalExpression logical => logical.Operator switch {
                ScimLogicalOperator.And => Matches(logical.Left, resource) && Matches(logical.Right, resource),
                ScimLogicalOperator.Or  => Matches(logical.Left, resource) || Matches(logical.Right, resource),
                _                       => false,
            },
            ScimNotExpression negation     => !Matches(negation.Operand, resource),
            ScimAttributeExpression test   => Matches(test, resource),
            ScimValuePathExpression value  => Matches(value, resource),
            _                              => false,
        };
    }

    private static bool Matches(ScimAttributeExpression filter, JsonObject resource) {
        var values = ReadValues(resource, filter.Path).ToList();
        if (filter.Operator == ScimComparisonOperator.Pr) {
            return values.Any(Present);
        }

        return values.Any(value => Compare(value, filter.Path.Attribute, filter.Operator, filter.Value));
    }

    private static bool Matches(ScimValuePathExpression filter, JsonObject resource) {
        var container = Container(resource, filter.Path.SchemaUri);
        if (container is null || !TryGet(container, filter.Path.Name, out var node) || node is not JsonArray values) {
            return false;
        }

        return values.OfType<JsonObject>().Any(value => Matches(filter.Filter, value));
    }

    private static IEnumerable<JsonNode?> ReadValues(JsonObject resource, ScimAttributePath path) {
        var container = Container(resource, path.SchemaUri);
        if (container is null || !TryGet(container, path.Name, out var node) || node is null) {
            yield break;
        }

        if (path.Parent is null) {
            if (path.Attribute.MultiValued && node is JsonArray array) {
                foreach (var value in array) {
                    yield return value;
                }
            } else {
                yield return node;
            }

            yield break;
        }

        if (path.SubAttribute is not { } subAttribute) {
            yield break;
        }

        var parents = node is JsonArray parentArray ? parentArray : new JsonArray(node.DeepClone());
        foreach (var parent in parents.OfType<JsonObject>()) {
            if (TryGet(parent, subAttribute, out var value) && value is not null) {
                yield return value;
            }
        }
    }

    private static bool Present(JsonNode? value) {
        return value switch {
            null        => false,
            JsonArray a => a.Count > 0,
            JsonObject o => o.Count > 0,
            JsonValue v when v.TryGetValue<string>(out var text) => text.Length > 0,
            _           => true,
        };
    }

    private static bool Compare(JsonNode? actual, ScimAttribute attribute, ScimComparisonOperator comparison, JsonNode? expected) {
        if (actual is null || expected is null) {
            return comparison switch {
                ScimComparisonOperator.Eq => actual is null && expected is null,
                ScimComparisonOperator.Ne => actual is not null && expected is null,
                _                           => false,
            };
        }

        if (attribute.Type == ScimConstants.BooleanType) {
            if (!TryBoolean(actual, out var left) || !TryBoolean(expected, out var right)) {
                return false;
            }

            return comparison switch {
                ScimComparisonOperator.Eq => left == right,
                ScimComparisonOperator.Ne => left != right,
                _                           => false,
            };
        }

        if (attribute.Type is ScimConstants.DecimalType or ScimConstants.IntegerType) {
            if (!TryDecimal(actual, out var left) || !TryDecimal(expected, out var right)) {
                return false;
            }

            return Relational(left.CompareTo(right), comparison);
        }

        if (attribute.Type == ScimConstants.DateTimeType) {
            if (!TryDate(actual, out var left) || !TryDate(expected, out var right)) {
                return false;
            }

            return Relational(left.CompareTo(right), comparison);
        }

        if (!TryString(actual, out var actualText) || !TryString(expected, out var expectedText)) {
            return false;
        }

        var caseExact = attribute.CaseExact == true || attribute.Type is ScimConstants.ReferenceType or ScimConstants.BinaryType;
        var comparer = caseExact ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        return comparison switch {
            ScimComparisonOperator.Eq => comparer.Equals(actualText, expectedText),
            ScimComparisonOperator.Ne => !comparer.Equals(actualText, expectedText),
            ScimComparisonOperator.Co => actualText.Contains(expectedText, caseExact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            ScimComparisonOperator.Sw => actualText.StartsWith(expectedText, caseExact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            ScimComparisonOperator.Ew => actualText.EndsWith(expectedText, caseExact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            ScimComparisonOperator.Gt => comparer.Compare(actualText, expectedText) > 0,
            ScimComparisonOperator.Ge => comparer.Compare(actualText, expectedText) >= 0,
            ScimComparisonOperator.Lt => comparer.Compare(actualText, expectedText) < 0,
            ScimComparisonOperator.Le => comparer.Compare(actualText, expectedText) <= 0,
            _                           => false,
        };
    }

    private static bool Relational(int result, ScimComparisonOperator comparison) {
        return comparison switch {
            ScimComparisonOperator.Eq => result == 0,
            ScimComparisonOperator.Ne => result != 0,
            ScimComparisonOperator.Gt => result > 0,
            ScimComparisonOperator.Ge => result >= 0,
            ScimComparisonOperator.Lt => result < 0,
            ScimComparisonOperator.Le => result <= 0,
            _                           => false,
        };
    }

    private static bool TryString(JsonNode value, out string text) {
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var result)) {
            text = result;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryBoolean(JsonNode value, out bool result) {
        if (value is JsonValue scalar && scalar.TryGetValue<bool>(out result)) {
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryDecimal(JsonNode value, out decimal result) {
        if (value is JsonValue scalar && scalar.TryGetValue<decimal>(out result)) {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryDate(JsonNode value, out DateTimeOffset result) {
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result)) {
            return true;
        }

        result = default;
        return false;
    }
}
