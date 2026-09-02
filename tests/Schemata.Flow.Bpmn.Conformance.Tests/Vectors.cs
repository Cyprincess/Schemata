using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Schemata.Flow.Bpmn.Conformance.Tests;

public static class Vectors
{
    private static readonly Regex CaseIdPattern = new(@"^[A-C]\.\d+\.\d+(?:\.\d+)?", RegexOptions.Compiled);

    public static IEnumerable<object[]> AllVectors() {
        return EnumerateVectors()
              .Where(path => !PendingCatalog.IsPending(path, out _))
              .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> PendingVectors() {
        return EnumerateVectors()
              .Where(path => PendingCatalog.IsPending(path, out _))
              .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> FastSubset() {
        return EnumerateVectors()
              .Where(path => !PendingCatalog.IsPending(path, out _))
              .GroupBy(CaseGroup)
              .Select(group => group.FirstOrDefault(IsReferenceVector) ?? group.First())
              .Select(path => new object[] { path });
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

    // Cross-tool variants of one case id are the same model serialized by different tools; the fast
    // loop executes one representative per case id while AllVectors keeps every dialect.
    private static string CaseGroup(string vectorPath) {
        var match = CaseIdPattern.Match(Path.GetFileNameWithoutExtension(vectorPath));
        return match.Success ? match.Value : vectorPath;
    }

    private static bool IsReferenceVector(string vectorPath) {
        return vectorPath.StartsWith("Reference/", StringComparison.OrdinalIgnoreCase);
    }
}
