namespace Schemata.Modular;

/// <summary>Options keys shared by the Modular builder, features, and service extensions.</summary>
internal static class ModularConstants
{
    /// <summary>Options key carrying an explicit runner instance recorded by <c>UseModular(instance)</c>.</summary>
    public const string PendingRunnerKey = "Schemata.Modular.PendingRunner";

    /// <summary>Options key carrying the committed module selection.</summary>
    public const string SelectionKey = "Schemata.Modular.Selection";
}
