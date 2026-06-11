using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Schemata.Common;

namespace Schemata.Mapping.Skeleton;

/// <summary>
///     Validated tree representation of AIP-161 field-mask paths.
/// </summary>
/// <remarks>
///     AIP-161 uses dot-separated paths to traverse fields. This parser stores each segment as a CLR
///     property name and validates segments through <see cref="AppDomainTypeCache" />.
/// </remarks>
public sealed class MaskTree
{
    private readonly Dictionary<string, MaskNode> _children = new(StringComparer.Ordinal);

    private MaskTree(Type rootType) { RootType = rootType; }

    /// <summary>
    ///     The CLR type at the root of the mask.
    /// </summary>
    public Type RootType { get; }

    /// <summary>
    ///     Top-level mask nodes keyed by CLR property name.
    /// </summary>
    public IReadOnlyDictionary<string, MaskNode> Children => new ReadOnlyDictionary<string, MaskNode>(_children);

    /// <summary>
    ///     Parses comma-separated wire-format paths into a validated CLR-name tree.
    /// </summary>
    /// <param name="rootType">The type where path validation starts.</param>
    /// <param name="mask">Comma-separated AIP-161 field paths.</param>
    /// <param name="allowCollectionTraversal">Whether nested paths may traverse collection elements.</param>
    /// <returns>The parsed mask tree.</returns>
    /// <exception cref="ArgumentException">Thrown when a path cannot be resolved from <paramref name="rootType" />.</exception>
    public static MaskTree FromWire(Type rootType, string mask, bool allowCollectionTraversal) {
        return Parse(rootType, Split(mask), allowCollectionTraversal, SchemataNaming.ToClrMemberName);
    }

    /// <summary>
    ///     Parses CLR-name paths into a validated AIP-161 field-mask tree.
    /// </summary>
    /// <param name="rootType">The type where path validation starts.</param>
    /// <param name="paths">CLR property paths, optionally containing dot traversal.</param>
    /// <param name="allowCollectionTraversal">Whether nested paths may traverse collection elements.</param>
    /// <returns>The parsed mask tree.</returns>
    /// <exception cref="ArgumentException">Thrown when a path cannot be resolved from <paramref name="rootType" />.</exception>
    public static MaskTree FromClr(Type rootType, IEnumerable<string> paths, bool allowCollectionTraversal) {
        return Parse(rootType, paths, allowCollectionTraversal, static s => s);
    }

    /// <summary>
    ///     Enumerates leaf paths in CLR-name dot format.
    /// </summary>
    /// <returns>Leaf paths.</returns>
    public IEnumerable<string> LeafPaths() {
        foreach (var child in _children.Values) {
            foreach (var path in child.LeafPaths()) {
                yield return path;
            }
        }
    }

    private static MaskTree Parse(
        Type                 rootType,
        IEnumerable<string>  paths,
        bool                 allowCollectionTraversal,
        Func<string, string> convert
    ) {
        var tree = new MaskTree(rootType);

        foreach (var raw in paths) {
            var path = raw.Trim();
            if (path.Length == 0) {
                continue;
            }

            AddPath(tree._children, rootType, path, path.Split('.'), 0, allowCollectionTraversal, convert);
        }

        return tree;
    }

    private static void AddPath(
        Dictionary<string, MaskNode> children,
        Type                         currentType,
        string                       originalPath,
        string[]                     segments,
        int                          index,
        bool                         allowCollectionTraversal,
        Func<string, string>         convert
    ) {
        var raw = segments[index].Trim();
        if (raw.Length == 0 || IsIndexSegment(raw)) {
            throw new ArgumentException($"The field-mask path `{originalPath}` contains an invalid segment.", nameof(originalPath));
        }

        var clr        = convert(raw);
        var properties = AppDomainTypeCache.GetProperties(currentType);
        if (!properties.TryGetValue(clr, out var property)) {
            throw new ArgumentException($"The field-mask path `{originalPath}` contains an unknown segment `{raw}`.", nameof(originalPath));
        }

        if (!children.TryGetValue(property.Name, out var node)) {
            node                    = new(property.Name, property, GetTraversalType(property.PropertyType));
            children[property.Name] = node;
        }

        if (index == segments.Length - 1) {
            return;
        }

        var nextType = property.PropertyType;
        if (TryGetEnumerableElement(nextType, out var elementType)) {
            if (!allowCollectionTraversal) {
                throw new ArgumentException(
                    $"The field-mask path `{originalPath}` traverses a collection field.",
                    nameof(originalPath));
            }

            nextType = elementType;
        }

        if (IsLeafType(nextType)) {
            throw new ArgumentException($"The field-mask path `{originalPath}` traverses a leaf field.", nameof(originalPath));
        }

        AddPath(node.MutableChildren, nextType, originalPath, segments, index + 1, allowCollectionTraversal, convert);
    }

    private static IEnumerable<string> Split(string mask) {
        return mask.Split(',');
    }

    private static bool IsIndexSegment(string segment) {
        return segment.Contains('[') || segment.Contains(']') || segment.All(char.IsDigit);
    }

    private static Type GetTraversalType(Type type) {
        return TryGetEnumerableElement(type, out var elementType) ? elementType : type;
    }

    private static bool IsLeafType(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan);
    }

    private static bool TryGetEnumerableElement(Type type, out Type elementType) {
        if (type == typeof(string)) {
            elementType = type;
            return false;
        }

        if (type.IsArray) {
            elementType = type.GetElementType()!;
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        var enumerable = type.GetInterfaces()
                             .FirstOrDefault(i => i.IsGenericType
                                               && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is null) {
            elementType = type;
            return false;
        }

        elementType = enumerable.GetGenericArguments()[0];
        return true;
    }

    /// <summary>
    ///     A validated field-mask segment stored as a CLR property.
    /// </summary>
    public sealed class MaskNode
    {
        internal MaskNode(string name, PropertyInfo property, Type traversalType) {
            Name          = name;
            Property      = property;
            TraversalType = traversalType;
        }

        private readonly Dictionary<string, MaskNode> _children = new(StringComparer.Ordinal);

        /// <summary>
        ///     CLR property name for this segment.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Property metadata for this segment.
        /// </summary>
        public PropertyInfo Property { get; }

        /// <summary>
        ///     The type used to validate child segments.
        /// </summary>
        public Type TraversalType { get; }

        /// <summary>
        ///     Child nodes keyed by CLR property name. Empty children mean this node is a leaf.
        /// </summary>
        public IReadOnlyDictionary<string, MaskNode> Children => new ReadOnlyDictionary<string, MaskNode>(_children);

        /// <summary>
        ///     Indicates that this node names a whole field subtree.
        /// </summary>
        public bool IsLeaf => _children.Count == 0;

        internal Dictionary<string, MaskNode> MutableChildren => _children;

        internal IEnumerable<string> LeafPaths() {
            if (IsLeaf) {
                yield return Name;
                yield break;
            }

            foreach (var child in _children.Values) {
                foreach (var path in child.LeafPaths()) {
                    yield return Name + "." + path;
                }
            }
        }
    }
}
