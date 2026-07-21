namespace Schemata.Modeling.Generator;

internal static partial class Parser
{
    private static string NormalizeOption(string input) {
        return input.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }
}
