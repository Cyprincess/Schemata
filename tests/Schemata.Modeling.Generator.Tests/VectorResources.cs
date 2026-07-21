using System.IO;
using System.Reflection;

namespace Schemata.Modeling.Generator.Tests;

internal static class VectorResources
{
    internal const string Vector1Skm = "Schemata.Modeling.Generator.Tests.vector1.skm";

    private static readonly Assembly Assembly = typeof(VectorResources).Assembly;

    internal static string ReadText(string logicalName) {
        using var stream = Assembly.GetManifestResourceStream(logicalName);
        if (stream is null) {
            var available = string.Join(", ", Assembly.GetManifestResourceNames());
            throw new FileNotFoundException($"Embedded resource '{logicalName}' not found. Available: {available}", logicalName);
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
