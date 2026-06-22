using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Reverse-resolves an entity type from a resource name against the registered
///     <see cref="SchemataResourceOptions.Resources" />. Two indexes are built once on first use:
///     a collection-name dictionary for bare-segment lookup, and a pattern trie that matches a
///     full canonical name segment-by-segment with placeholder backtracking.
/// </summary>
public sealed class DefaultResourceTypeResolver : IResourceTypeResolver
{
    private static readonly char[] Separator = ['/'];

    private readonly Lazy<Dictionary<string, Type>>    _byCollection;
    private readonly Lazy<TrieNode>                    _trie;
    private readonly IOptions<SchemataResourceOptions> _options;

    /// <summary>Creates the resolver over the registered resource descriptors.</summary>
    /// <param name="options">The registered resources.</param>
    public DefaultResourceTypeResolver(IOptions<SchemataResourceOptions> options) {
        _options      = options;
        _byCollection = new(BuildCollectionIndex);
        _trie         = new(BuildPatternTrie);
    }

    #region IResourceTypeResolver Members

    public Type? Resolve(string canonicalName) {
        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return null;
        }

        var parts = canonicalName.Split(Separator);
        var entity = Walk(_trie.Value, parts, 0);
        if (entity is not null) {
            return entity;
        }

        return ResolveCollection(canonicalName);
    }

    public Type? ResolveCollection(string collection) {
        if (string.IsNullOrEmpty(collection)) {
            return null;
        }

        return _byCollection.Value.GetValueOrDefault(collection);
    }

    #endregion

    private static Type? Walk(TrieNode node, string[] parts, int index) {
        while (true) {
            if (index == parts.Length) {
                return node.Entity;
            }

            var segment = parts[index];
            if (segment.Length == 0) {
                return null;
            }

            if (node.Literal is not null && node.Literal.TryGetValue(segment, out var literalChild)) {
                var matched = Walk(literalChild, parts, index + 1);
                if (matched is not null) {
                    return matched;
                }
            }

            if (node.Placeholder is null) {
                return null;
            }

            node  =  node.Placeholder;
            index += 1;
        }
    }

    private Dictionary<string, Type> BuildCollectionIndex() {
        var index = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var resource in _options.Value.Resources.Values) {
            var entity     = resource.Entity;
            var collection = ResourceNameDescriptor.ForType(entity).Collection;
            if (!string.IsNullOrEmpty(collection)) {
                index.TryAdd(collection, entity);
            }
        }

        return index;
    }

    private TrieNode BuildPatternTrie() {
        var root = new TrieNode();
        foreach (var resource in _options.Value.Resources.Values) {
            var entity     = resource.Entity;
            var descriptor = ResourceNameDescriptor.ForType(entity);
            var pattern    = descriptor.Pattern;
            if (string.IsNullOrEmpty(pattern)) {
                continue;
            }

            var segments = pattern!.Split(Separator);
            // Only patterns whose leaf is a placeholder participate, matching
            // ResourceNameDescriptor.ParseCanonicalName's contract: a bare collection name
            // is served by ResolveCollection rather than full-pattern matching.
            var leaf = segments[^1];
            if (!IsPlaceholder(leaf)) {
                continue;
            }

            var node = root;
            foreach (var part in segments) {
                if (IsPlaceholder(part)) {
                    node.Placeholder ??= new();
                    node             =   node.Placeholder;
                } else {
                    node.Literal ??= new(StringComparer.OrdinalIgnoreCase);
                    if (!node.Literal.TryGetValue(part, out var child)) {
                        child              = new();
                        node.Literal[part] = child;
                    }

                    node = child;
                }
            }

            // First registration wins on terminal conflicts.
            node.Entity ??= entity;
        }

        return root;
    }

    private static bool IsPlaceholder(string segment) {
        return segment.Length > 2 && segment[0] == '{' && segment[^1] == '}';
    }

    #region Nested type: TrieNode

    private sealed class TrieNode
    {
        public Dictionary<string, TrieNode>? Literal;
        public TrieNode?                     Placeholder;
        public Type?                         Entity;
    }

    #endregion
}
