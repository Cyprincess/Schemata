using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Schemata.Flow.Bpmn.Conformance.Tests;

public static class Vectors
{
    private static readonly string[] FastVectorHints = [
        "Reference",
        "B.1.0",
        "A.1.0",
    ];

    public static IEnumerable<object[]> AllVectors() {
        return EnumerateVectors()
              .Where(path => !PendingCatalog.IsPending(path, out _))
              .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> FastSubset() {
        var vectors = EnumerateVectors()
                     .Where(path => FastVectorHints.Any(hint => path.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                     .Where(path => !PendingCatalog.IsPending(path, out _))
                     .Take(50)
                     .ToList();

        if (vectors.Count == 0) {
            vectors = EnumerateVectors().Where(path => !PendingCatalog.IsPending(path, out _)).Take(10).ToList();
        }

        return vectors.Select(path => new object[] { path });
    }

    internal static string SpecsRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../specs/bpmn"));
    }

    internal static IEnumerable<string> EnumerateVectors() {
        var root = SpecsRoot();
        if (!Directory.Exists(root)) {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.bpmn", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)) {
            yield return Normalize(Path.GetRelativePath(root, path));
        }
    }

    internal static string AbsolutePath(string vectorPath) {
        return Path.GetFullPath(Path.Combine(SpecsRoot(), Normalize(vectorPath)));
    }

    internal static string Normalize(string path) {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
