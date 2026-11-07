using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Schemata.Scim.Models;

namespace Schemata.Scim.Services;

/// <summary>A node in the SCIM filter abstract syntax tree.</summary>
public abstract record ScimFilterExpression;

public enum ScimLogicalOperator { And, Or }

public enum ScimComparisonOperator { Eq, Ne, Co, Sw, Ew, Pr, Gt, Ge, Lt, Le }

/// <summary>A schema-resolved attribute name in standard SCIM notation.</summary>
public sealed record ScimAttributePath(
    string? SchemaUri,
    string Name,
    string? SubAttribute,
    ScimAttribute Attribute,
    ScimAttribute? Parent
);

/// <summary>A comparison or presence test against an attribute.</summary>
public sealed record ScimAttributeExpression(
    ScimAttributePath Path,
    ScimComparisonOperator Operator,
    JsonNode? Value
) : ScimFilterExpression;

/// <summary>A logical AND/OR expression.</summary>
public sealed record ScimLogicalExpression(
    ScimLogicalOperator Operator,
    ScimFilterExpression Left,
    ScimFilterExpression Right
) : ScimFilterExpression;

/// <summary>A logical negation.</summary>
public sealed record ScimNotExpression(ScimFilterExpression Operand) : ScimFilterExpression;

/// <summary>A complex multi-valued attribute selection.</summary>
public sealed record ScimValuePathExpression(
    ScimAttributePath Path,
    ScimFilterExpression Filter
) : ScimFilterExpression;

/// <summary>A PATCH path with an optional value selection and selected sub-attribute.</summary>
public sealed record ScimPatchPath(
    ScimAttributePath Path,
    ScimFilterExpression? Filter,
    string? SelectedSubAttribute,
    ScimAttribute? SelectedAttribute
);

/// <summary>Parses the RFC 7644 filter and attribute-path grammar into schema-resolved nodes.</summary>
public static class ScimFilterParser
{
    public static ScimFilterExpression Parse(string expression, params ScimSchema[] schemas) {
        return Parse(expression, (IReadOnlyList<ScimSchema>)schemas);
    }

    public static ScimFilterExpression Parse(string expression, IReadOnlyList<ScimSchema> schemas, int maxDepth = 32) {
        if (string.IsNullOrWhiteSpace(expression)) {
            throw Invalid("The filter must contain an expression.");
        }

        var parser = new Parser(expression, schemas, maxDepth);
        var result = parser.ParseOr();
        parser.SkipWhitespace();
        if (!parser.End) {
            throw Invalid("The filter contains unexpected trailing input.");
        }

        return result;
    }

    public static ScimAttributePath ParseAttributePath(string path, IReadOnlyList<ScimSchema> schemas) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw InvalidPath();
        }

        return ResolvePath(path, schemas, null);
    }

    public static ScimPatchPath ParsePatchPath(string path, IReadOnlyList<ScimSchema> schemas) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ScimProtocolException(400, ScimConstants.InvalidPath, "The PATCH path is required.");
        }

        var open = path.IndexOf('[');
        if (open < 0) {
            var attribute = ParseAttributePath(path, schemas);
            return new(attribute, null, attribute.SubAttribute, attribute.SubAttribute is null ? null : attribute.Attribute);
        }

        var close = FindClosingBracket(path, open);
        if (close < 0) {
            throw new ScimProtocolException(400, ScimConstants.InvalidFilter, "The valuePath filter is not closed.");
        }

        var attributeName = path[..open];
        var parent = ParseAttributePath(attributeName, schemas);
        if (parent.SubAttribute is not null || !parent.Attribute.MultiValued || parent.Attribute.Type != ScimConstants.ComplexType) {
            throw new ScimProtocolException(400, ScimConstants.InvalidPath, "A valuePath requires a complex multi-valued attribute.");
        }

        var filterText = path.Substring(open + 1, close - open - 1);
        var nestedSchemas = new[] {
            new ScimSchema { Id = null, Attributes = parent.Attribute.SubAttributes ?? [] },
        };

        ScimFilterExpression filter;
        try {
            filter = Parse(filterText, nestedSchemas);
        } catch (ScimProtocolException error) when (error.ScimType == ScimConstants.InvalidPath) {
            throw new ScimProtocolException(400, ScimConstants.InvalidFilter, "The valuePath filter is invalid.");
        }

        var remainder = path[(close + 1)..];
        if (remainder.Length == 0) {
            return new(parent, filter, null, null);
        }

        if (remainder[0] != '.' || remainder.Length == 1) {
            throw new ScimProtocolException(400, ScimConstants.InvalidPath, "The PATCH valuePath has an invalid sub-attribute.");
        }

        var selectedName = remainder[1..];
        var selected = parent.Attribute.SubAttributes?.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (selected is null) {
            throw new ScimProtocolException(400, ScimConstants.InvalidPath, "The PATCH path names an unknown sub-attribute.");
        }

        return new(parent, filter, selected.Name, selected);
    }

    private static int FindClosingBracket(string path, int open) {
        var quoted = false;
        var escaped = false;
        for (var index = open + 1; index < path.Length; index++) {
            var current = path[index];
            if (escaped) {
                escaped = false;
                continue;
            }

            if (quoted && current == '\\') {
                escaped = true;
                continue;
            }

            if (current == '"') {
                quoted = !quoted;
                continue;
            }

            if (!quoted && current == ']') {
                return index;
            }
        }

        return -1;
    }

    private static ScimAttributePath ResolvePath(string value, IReadOnlyList<ScimSchema> schemas, ScimAttribute? context) {
        string? schemaUri = null;
        var name = value;
        if (context is null && value.StartsWith("urn:", StringComparison.Ordinal)) {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1) {
                throw InvalidPath();
            }

            schemaUri = value[..separator];
            name      = value[(separator + 1)..];
        }

        var parts = name.Split('.');
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrWhiteSpace)) {
            throw InvalidPath();
        }

        if (context is not null) {
            if (schemaUri is not null) {
                throw InvalidPath();
            }

            var nested = FindAttribute(context.SubAttributes, parts[0]);
            if (nested is null) {
                throw InvalidPath();
            }

            if (parts.Length == 1) {
                return new(null, nested.Name, null, nested, null);
            }

            var nestedSub = FindAttribute(nested.SubAttributes, parts[1]);
            return nestedSub is null ? throw InvalidPath() : new(null, nested.Name, nestedSub.Name, nestedSub, nested);
        }

        ScimSchema? schema = null;
        ScimAttribute? attribute = null;
        if (schemaUri is not null) {
            schema = schemas.FirstOrDefault(item => string.Equals(item.Id, schemaUri, StringComparison.Ordinal));
            if (schema is null) {
                throw InvalidPath();
            }

            attribute = FindAttribute(schema.Attributes, parts[0]);
        } else {
            foreach (var candidate in schemas) {
                attribute = FindAttribute(candidate.Attributes, parts[0]);
                if (attribute is not null) {
                    schema = candidate;
                    break;
                }
            }
        }

        if ((schema is null || attribute is null) && schemas.Count > 0
            && (schemaUri is null || string.Equals(schemaUri, schemas[0].Id, StringComparison.Ordinal))) {
            attribute = FindAttribute(ScimSchemaRegistry.CommonAttributes(), parts[0]);
            if (attribute is not null) {
                schema = schemas[0];
            }
        }

        if (schema is null || attribute is null) {
            throw InvalidPath();
        }

        if (parts.Length == 1) {
            return new(schema.Id, attribute.Name, null, attribute, null);
        }

        if (attribute.Type != ScimConstants.ComplexType) {
            throw InvalidPath();
        }

        var subAttribute = FindAttribute(attribute.SubAttributes, parts[1]);
        return subAttribute is null
            ? throw InvalidPath()
            : new ScimAttributePath(schema.Id, attribute.Name, subAttribute.Name, subAttribute, attribute);
    }

    private static ScimAttribute? FindAttribute(IEnumerable<ScimAttribute>? attributes, string name) {
        return attributes?.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static ScimProtocolException Invalid(string detail) {
        return new(400, ScimConstants.InvalidFilter, detail);
    }

    private static ScimProtocolException InvalidPath() {
        return new(400, ScimConstants.InvalidPath, "The attribute path is invalid or is not present in the resource schema.");
    }

    private sealed class Parser(string input, IReadOnlyList<ScimSchema> schemas, int maxDepth)
    {
        private int _position;
        private int _depth;

        public bool End => _position >= input.Length;

        public ScimFilterExpression ParseOr() {
            var left = ParseAnd();
            while (TryOperator("or")) {
                left = new ScimLogicalExpression(ScimLogicalOperator.Or, left, ParseAnd());
            }

            return left;
        }

        private ScimFilterExpression ParseAnd() {
            var left = ParseNot();
            while (TryOperator("and")) {
                left = new ScimLogicalExpression(ScimLogicalOperator.And, left, ParseNot());
            }

            return left;
        }

        private ScimFilterExpression ParseNot() {
            SkipWhitespace();
            if (!TryWord("not")) {
                return ParsePrimary();
            }

            Enter();
            var operand = ParseNot();
            _depth--;
            return new ScimNotExpression(operand);
        }

        private ScimFilterExpression ParsePrimary() {
            SkipWhitespace();
            if (End) {
                throw Invalid("The filter ended before an expression was supplied.");
            }

            if (Peek() == '(') {
                _position++;
                Enter();
                var nested = ParseOr();
                SkipWhitespace();
                Expect(')');
                _depth--;
                return nested;
            }

            var pathText = ReadPath();
            ScimAttributePath path;
            try {
                path = ResolvePath(pathText, schemas, null);
            } catch (ScimProtocolException error) when (error.ScimType == ScimConstants.InvalidPath) {
                throw Invalid("The filter names an unknown attribute.");
            }
            SkipWhitespace();
            if (!End && Peek() == '[') {
                if (path.SubAttribute is not null || !path.Attribute.MultiValued || path.Attribute.Type != ScimConstants.ComplexType) {
                    throw Invalid("A valuePath requires a complex multi-valued attribute.");
                }

                _position++;
                Enter();
                var nestedSchemas = new[] {
                    new ScimSchema { Id = null, Attributes = path.Attribute.SubAttributes ?? [] },
                };
                var nestedText = ReadBracketContent();
                var nestedParser = new Parser(nestedText, nestedSchemas, maxDepth) { _depth = _depth };
                var filter = nestedParser.ParseOr();
                nestedParser.SkipWhitespace();
                if (!nestedParser.End) {
                    throw Invalid("The valuePath filter contains unexpected trailing input.");
                }
                _depth = nestedParser._depth;
                Expect(']');
                _depth--;
                return new ScimValuePathExpression(path, filter);
            }

            var operatorName = ReadWord().ToLowerInvariant();
            if (operatorName == "pr") {
                return new ScimAttributeExpression(path, ScimComparisonOperator.Pr, null);
            }

            var comparison = operatorName switch {
                "eq" => ScimComparisonOperator.Eq,
                "ne" => ScimComparisonOperator.Ne,
                "co" => ScimComparisonOperator.Co,
                "sw" => ScimComparisonOperator.Sw,
                "ew" => ScimComparisonOperator.Ew,
                "gt" => ScimComparisonOperator.Gt,
                "ge" => ScimComparisonOperator.Ge,
                "lt" => ScimComparisonOperator.Lt,
                "le" => ScimComparisonOperator.Le,
                _    => throw Invalid($"The filter operator '{operatorName}' is not supported."),
            };
            ValidateOperator(path.Attribute, comparison);
            if (End || !char.IsWhiteSpace(Peek())) {
                throw Invalid("The comparison operator and value must be separated by whitespace.");
            }
            var value = ReadValue(path.Attribute);
            return new ScimAttributeExpression(path, comparison, value);
        }

        private JsonNode? ReadValue(ScimAttribute attribute) {
            SkipWhitespace();
            if (End) {
                throw Invalid("The comparison value is missing.");
            }

            JsonNode? value;
            if (Peek() == '"') {
                value = JsonValue.Create(ReadString());
            } else {
                var token = ReadLiteral();
                if (token == "true") {
                    value = JsonValue.Create(true);
                } else if (token == "false") {
                    value = JsonValue.Create(false);
                } else if (token == "null") {
                    value = null;
                } else {
                    if (attribute.Type == ScimConstants.IntegerType && token.IndexOfAny(['.', 'e', 'E']) >= 0) {
                        throw Invalid("An integer comparison value cannot contain fractional or exponent parts.");
                    }

                    try {
                        using var document = JsonDocument.Parse(token);
                        if (document.RootElement.ValueKind != JsonValueKind.Number) {
                            throw Invalid("The comparison value is not valid JSON.");
                        }

                        value = JsonNode.Parse(token);
                    } catch (JsonException) {
                        throw Invalid("The comparison value is not valid JSON.");
                    }
                }
            }

            ValidateValue(attribute, value);
            return value;
        }

        private static void ValidateOperator(ScimAttribute attribute, ScimComparisonOperator comparison) {
            var stringOperation = comparison is ScimComparisonOperator.Co or ScimComparisonOperator.Sw or ScimComparisonOperator.Ew;
            var relational = comparison is ScimComparisonOperator.Gt or ScimComparisonOperator.Ge
                or ScimComparisonOperator.Lt or ScimComparisonOperator.Le;
            if (stringOperation && attribute.Type != ScimConstants.StringType
                || relational && attribute.Type is ScimConstants.BooleanType or ScimConstants.BinaryType or ScimConstants.ComplexType
                || comparison != ScimComparisonOperator.Pr && attribute.Type == ScimConstants.ComplexType) {
                throw Invalid("The attribute and filter operator combination is not supported.");
            }
        }

        private static void ValidateValue(ScimAttribute attribute, JsonNode? value) {
            if (value is null) {
                return;
            }

            var valid = attribute.Type switch {
                ScimConstants.BooleanType => value is JsonValue scalar && scalar.TryGetValue<bool>(out _),
                ScimConstants.DecimalType or ScimConstants.IntegerType => value is JsonValue scalar && scalar.TryGetValue<decimal>(out _),
                ScimConstants.DateTimeType => value is JsonValue scalar && scalar.TryGetValue<string>(out var text)
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
                ScimConstants.StringType or ScimConstants.ReferenceType or ScimConstants.BinaryType =>
                    value is JsonValue scalar && scalar.TryGetValue<string>(out _),
                _ => false,
            };
            if (!valid || attribute.Type == ScimConstants.IntegerType && value is JsonValue integer
                && (!integer.TryGetValue<decimal>(out var integerValue) || decimal.Truncate(integerValue) != integerValue)) {
                throw Invalid("The comparison value does not match the attribute type.");
            }
        }

        private string ReadPath() {
            SkipWhitespace();
            var start = _position;
            while (!End && !char.IsWhiteSpace(Peek()) && Peek() is not '[' and not ']' and not '(' and not ')') {
                _position++;
            }

            if (start == _position) {
                throw Invalid("The attribute path is missing.");
            }

            return input[start.._position];
        }

        private string ReadWord() {
            SkipWhitespace();
            var start = _position;
            while (!End && char.IsLetter(Peek())) {
                _position++;
            }

            if (start == _position) {
                throw Invalid("The attribute operator is missing.");
            }

            return input[start.._position];
        }

        private string ReadLiteral() {
            var start = _position;
            while (!End && !char.IsWhiteSpace(Peek()) && Peek() is not ')' and not ']') {
                _position++;
            }

            return input[start.._position];
        }

        private string ReadString() {
            var start = _position++;
            var escaped = false;
            while (!End) {
                var current = Peek();
                _position++;
                if (escaped) {
                    escaped = false;
                    continue;
                }

                if (current == '\\') {
                    escaped = true;
                    continue;
                }

                if (current == '"') {
                    var token = input[start.._position];
                    try {
                        return JsonSerializer.Deserialize<string>(token) ?? string.Empty;
                    } catch (JsonException) {
                        throw Invalid("The filter string is not valid JSON.");
                    }
                }
            }

            throw Invalid("The filter string is not closed.");
        }

        private string ReadBracketContent() {
            var start = _position;
            var quoted = false;
            var escaped = false;
            var nested = 0;
            while (!End) {
                var current = Peek();
                if (escaped) {
                    escaped = false;
                    _position++;
                    continue;
                }

                if (quoted && current == '\\') {
                    escaped = true;
                    _position++;
                    continue;
                }

                if (current == '"') {
                    quoted = !quoted;
                    _position++;
                    continue;
                }

                if (!quoted) {
                    if (current == '[') {
                        nested++;
                    } else if (current == ']' && nested == 0) {
                        return input[start.._position];
                    } else if (current == ']') {
                        nested--;
                    }
                }

                _position++;
            }

            throw Invalid("The valuePath filter is not closed.");
        }

        private bool TryOperator(string value) {
            SkipWhitespace();
            if (!TryWord(value)) {
                return false;
            }

            return true;
        }

        private bool TryWord(string value) {
            if (_position + value.Length > input.Length
                || !input.AsSpan(_position, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            var end = _position + value.Length;
            if (end < input.Length && (char.IsLetterOrDigit(input[end]) || input[end] is '_' or '-')) {
                return false;
            }

            _position = end;
            return true;
        }

        private void Enter() {
            _depth++;
            if (_depth > maxDepth) {
                throw Invalid("The filter exceeds the maximum nesting depth.");
            }
        }

        private void Expect(char expected) {
            SkipWhitespace();
            if (End || Peek() != expected) {
                throw Invalid($"The filter expected '{expected}'.");
            }

            _position++;
        }

        public void SkipWhitespace() {
            while (!End && char.IsWhiteSpace(Peek())) {
                _position++;
            }
        }

        private char Peek() => input[_position];
    }
}
