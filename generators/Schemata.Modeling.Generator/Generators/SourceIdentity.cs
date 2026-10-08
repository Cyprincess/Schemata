using System.Text;

namespace Schemata.Modeling.Generator;

internal static class SourceIdentity
{
    internal static string Hint(string kind, string? ns, string name) {
        var identity = kind + ":" + (ns?.Length ?? 0) + ":" + ns + ":" + name;
        var result = new StringBuilder(identity.Length * 4);
        foreach (var character in identity) result.Append(((int)character).ToString("X4"));
        return result + ".g.cs";
    }
}
